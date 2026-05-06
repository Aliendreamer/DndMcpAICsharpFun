using System.Diagnostics;
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed class EntityExtractionOrchestrator(
    IIngestionTracker tracker,
    IDoclingPdfConverter docling,
    IPdfBookmarkReader bookmarks,
    IEntityExtractionLlmClient llm,
    ExtractionPromptBuilder promptBuilder,
    EntityCandidateScanner scanner,
    CanonicalJsonWriter writer,
    ExtractionErrorsFile errorsFile,
    ExtractionWarningsFile warningsFile,
    EntityReferenceResolver refResolver,
    ExtractionRetryPolicy retry,
    IOptions<EntityExtractionOptions> options,
    IOptions<OllamaOptions> ollamaOpts,
    ILogger<EntityExtractionOrchestrator> logger) : IEntityExtractionOrchestrator
{
    private readonly EntityExtractionOptions _opts = options.Value;
    private readonly OllamaOptions _ollama = ollamaOpts.Value;

    public async Task ExtractAsync(int bookId, bool force, CancellationToken ct)
    {
        var record = await tracker.GetByIdAsync(bookId, ct)
                     ?? throw new InvalidOperationException($"No ingestion record {bookId}");

        // Derive book slug (e.g. "phb14") from the display name via the same
        // slug builder used at ingest time, then strip the trailing type/name
        // segments — only the book prefix is meaningful here.
        var bookSlug = EntityIdSlug
            .For(record.DisplayName, EntityType.Class, "x")
            .Split('.')[0];

        var canonicalPath = Path.Combine(_opts.CanonicalDirectory, bookSlug + ".json");
        var errorsPath = Path.Combine(_opts.CanonicalDirectory, bookSlug + ".errors.json");
        var warningsPath = Path.Combine(_opts.CanonicalDirectory, bookSlug + ".warnings.json");

        if (File.Exists(canonicalPath) && !force)
            throw new InvalidOperationException(
                $"Canonical JSON already exists at {canonicalPath}; pass force=true to overwrite.");

        await tracker.MarkEntitiesExtractingAsync(bookId, ct);

        try
        {
            logger.LogInformation(
                "Entity extraction starting: book {BookId} ({DisplayName}), file {FilePath}",
                bookId, record.DisplayName, record.FilePath);

            // 1. Convert PDF via Docling (markdown + structured items).
            var doc = await docling.ConvertAsync(record.FilePath, ct);

            // 2. Read bookmarks → TocCategoryMap.
            var pdfBookmarks = bookmarks.ReadBookmarks(record.FilePath);
            var tocEntries = BookmarkTocMapper.Map(pdfBookmarks);
            var tocMap = new TocCategoryMap(tocEntries);

            // 3. Project Docling items into ScannerInputs. Use the most-recent
            //    heading text as the section title for following text/list items.
            var scannerInputs = BuildScannerInputs(doc.Items);
            var candidates = scanner.Scan(scannerInputs, tocMap).ToList();

            logger.LogInformation(
                "Entity extraction: {CandidateCount} candidates from {ItemCount} Docling items",
                candidates.Count, doc.Items.Count);

            // 4. Load schemas keyed by EntityType.
            var schemas = LoadSchemas();

            // 5. Iterate candidates: prompt the LLM, retry, collect.
            var extracted = new List<EntityEnvelope>(candidates.Count);
            var extractionErrors = new List<ExtractionErrorEntry>();

            var sw = Stopwatch.StartNew();
            var lastLog = TimeSpan.Zero;
            int success = 0;
            int failed = 0;

            for (int i = 0; i < candidates.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var candidate = candidates[i];

                if (!schemas.TryGetValue(candidate.Type, out var schema))
                {
                    logger.LogWarning(
                        "No schema for entity type {Type}; skipping candidate {Name}",
                        candidate.Type, candidate.DisplayName);
                    failed++;
                    continue;
                }

                var request = new ExtractionRequest(
                    SystemPrompt: promptBuilder.BuildSystemPrompt(record.DisplayName, record.Version, candidate.Type),
                    UserPrompt: promptBuilder.BuildUserPrompt(candidate),
                    ToolName: promptBuilder.ToolName(candidate.Type),
                    ToolDescription: promptBuilder.ToolDescription(candidate.Type),
                    ToolInputSchema: schema,
                    ModelId: _ollama.ChatModel,
                    MaxOutputTokens: 4096);

                var response = await retry.ExecuteAsync(
                    operation: (_, c) => llm.ExtractAsync(request, c),
                    isSuccess: r => r.Success,
                    ct);

                if (!response.Success || response.ToolInput is null)
                {
                    logger.LogWarning(
                        "Extraction failed for {Type} '{Name}' (page {Page}): {Error}",
                        candidate.Type, candidate.DisplayName, candidate.Page, response.ErrorMessage);
                    extractionErrors.Add(new ExtractionErrorEntry(
                        SourceEntityId: EntityIdSlug.For(record.DisplayName, candidate.Type, candidate.DisplayName),
                        FieldPath: "(extraction)",
                        MissingTargetId: "",
                        ErrorKind: "extraction_failure",
                        Detail: response.ErrorMessage));
                    failed++;
                    continue;
                }

                var id = EntityIdSlug.For(record.DisplayName, candidate.Type, candidate.DisplayName);
                var envelope = new EntityEnvelope(
                    Id: id,
                    Type: candidate.Type,
                    Name: candidate.DisplayName,
                    SourceBook: record.DisplayName,
                    Edition: record.Version,
                    Page: candidate.Page,
                    FirstAppearedIn: new FirstAppearance(record.DisplayName, record.Version, candidate.Page),
                    RevisedIn: Array.Empty<Revision>(),
                    SettingTags: Array.Empty<string>(),
                    CanonicalText: string.Empty,
                    Fields: response.ToolInput.Value);

                extracted.Add(envelope);
                success++;

                if (sw.Elapsed - lastLog >= TimeSpan.FromSeconds(_opts.ProgressLogIntervalSeconds))
                {
                    logger.LogInformation(
                        "Extraction progress: {Done}/{Total} ({Success} ok, {Failed} failed)",
                        i + 1, candidates.Count, success, failed);
                    lastLog = sw.Elapsed;
                }
            }

            // 6. Resolve references; classify intra vs inter book.
            var refWarnings = refResolver.Resolve(extracted).ToList();
            var classifier = new IntraBookReferenceClassifier(bookSlug);
            var (intra, inter) = classifier.Partition(refWarnings);

            // 7. Drop any extracted entity that is the source of an intra-book
            //    dangling reference; record those as errors.
            if (intra.Count > 0)
            {
                var offenders = intra.Select(w => w.SourceEntityId)
                    .ToHashSet(StringComparer.Ordinal);
                extracted.RemoveAll(e => offenders.Contains(e.Id));

                foreach (var w in intra)
                {
                    extractionErrors.Add(new ExtractionErrorEntry(
                        SourceEntityId: w.SourceEntityId,
                        FieldPath: w.FieldPath,
                        MissingTargetId: w.MissingTargetId,
                        ErrorKind: "intra_book_dangling_ref",
                        Detail: null));
                }
            }

            var interWarnings = inter
                .Select(w => new ExtractionWarningEntry(
                    SourceEntityId: w.SourceEntityId,
                    FieldPath: w.FieldPath,
                    MissingTargetId: w.MissingTargetId,
                    WarningKind: "inter_book_dangling_ref"))
                .ToList();

            // 8. Write canonical JSON, errors, warnings.
            var canonicalFile = new CanonicalJsonFile(
                SchemaVersion: CanonicalJsonSchema.CurrentVersion,
                Book: new CanonicalBookMetadata(
                    SourceBook: record.DisplayName,
                    Edition: record.Version,
                    FileHash: record.FileHash,
                    DisplayName: record.DisplayName),
                Entities: extracted);

            await writer.WriteAsync(canonicalPath, canonicalFile, ct);
            await errorsFile.WriteAsync(errorsPath, extractionErrors, ct);
            await warningsFile.WriteAsync(warningsPath, interWarnings, ct);

            logger.LogInformation(
                "Entity extraction complete: book {BookId}, {Clean} clean / {Errors} errors / {Warnings} warnings",
                bookId, extracted.Count, extractionErrors.Count, interWarnings.Count);

            await tracker.MarkEntitiesExtractedAsync(bookId, extracted.Count, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Entity extraction failed for book {BookId}", bookId);
            try
            {
                await tracker.MarkEntitiesFailedAsync(bookId, ex.Message, CancellationToken.None);
            }
            catch (Exception trackerEx)
            {
                logger.LogError(trackerEx, "Failed to mark book {BookId} as EntitiesFailed", bookId);
            }
            throw;
        }
    }

    private static IList<ScannerInput> BuildScannerInputs(IReadOnlyList<DoclingItem> items)
    {
        var inputs = new List<ScannerInput>(items.Count);
        var currentSection = "(unknown)";
        foreach (var item in items)
        {
            var type = item.Type ?? string.Empty;
            // Treat any heading-shaped item as a section boundary.
            if (type.StartsWith("section", StringComparison.OrdinalIgnoreCase) ||
                type.StartsWith("heading", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("title", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(item.Text))
                    currentSection = item.Text.Trim();
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.Text)) continue;
            inputs.Add(new ScannerInput(currentSection, item.PageNumber, item.Text));
        }
        return inputs;
    }

    private Dictionary<EntityType, JsonElement> LoadSchemas()
    {
        var dict = new Dictionary<EntityType, JsonElement>();
        foreach (var type in Enum.GetValues<EntityType>())
        {
            var path = Path.Combine(_opts.SchemasDirectory, $"{type}Fields.schema.json");
            if (!File.Exists(path))
            {
                logger.LogDebug("Schema file not found for {Type} at {Path}; type will be skipped", type, path);
                continue;
            }
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            dict[type] = doc.RootElement.Clone();
        }
        return dict;
    }
}
