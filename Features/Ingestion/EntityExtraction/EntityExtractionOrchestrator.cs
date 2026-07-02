using System.Diagnostics;
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed class EntityExtractionOrchestrator(
    IIngestionTracker tracker,
    BookSourceRegistry registry,
    IPdfStructureConverter converter,
    EntityCandidateBuilder candidateBuilder,
    CanonicalJsonWriter writer,
    ExtractionErrorsFile errorsFile,
    ExtractionWarningsFile warningsFile,
    ExtractionDeclinedFile declinedFile,
    EntityReferenceResolver refResolver,
    EntitySchemaProvider schemaProvider,
    ExtractionCheckpointStore checkpointStore,
    EntityExtractionRunner runner,
    IOptions<EntityExtractionOptions> options,
    ILogger<EntityExtractionOrchestrator> logger,
    EntityNameMatcher? matcher = null) : IEntityExtractionOrchestrator
{
    private readonly EntityExtractionOptions _opts = options.Value;
    private readonly EntityNameMatcher? _matcher = matcher;

    public async Task ExtractAsync(int bookId, bool force, bool errorsOnly, CancellationToken ct)
    {
        var record = await tracker.GetByIdAsync(bookId, ct)
                     ?? throw new InvalidOperationException($"No ingestion record {bookId}");

        // Honour the 5etools source key for the slug/ids (PHB -> phb14), falling back to the
        // display name. Aligns the canonical file name and entity ids with the 5etools pipeline.
        var bookSlug = EntityIdSlug
            .For(ExtractionEntityIds.BookKey(record), EntityType.Class, "x")
            .Split('.')[0];

        var canonicalPath = Path.Combine(_opts.CanonicalDirectory, bookSlug + ".json");
        var errorsPath    = Path.Combine(_opts.CanonicalDirectory, bookSlug + ".errors.json");
        var warningsPath  = Path.Combine(_opts.CanonicalDirectory, bookSlug + ".warnings.json");

        if (!errorsOnly && File.Exists(canonicalPath) && !force)
            throw new InvalidOperationException(
                $"Canonical JSON already exists at {canonicalPath}; pass force=true to overwrite.");

        await tracker.MarkEntitiesExtractingAsync(bookId, ct);

        try
        {
            logger.LogInformation(
                "Entity extraction starting: book {BookId} ({DisplayName}), file {FilePath}, errorsOnly={ErrorsOnly}",
                bookId, record.DisplayName, record.FilePath, errorsOnly);

            // 1. Convert PDF via MinerU (markdown + structured items).
            var doc = await converter.ConvertAsync(record.FilePath, ct);

            // 2-3. Build the ordered candidate list (TOC classification, scanning, dedup, prefilter).
            var candidates = candidateBuilder.Build(doc, record, bookId);

            // 4. Load schemas keyed by EntityType.
            var schemas = schemaProvider.LoadSchemas();

            // 5. Dispatch to either errors-only or full extraction path.
            if (errorsOnly)
            {
                await RunErrorsOnlyAsync(
                    bookId, record, bookSlug, candidates, schemas,
                    canonicalPath, errorsPath, warningsPath, ct);
            }
            else
            {
                await RunFullExtractionAsync(
                    bookId, record, bookSlug, candidates, schemas,
                    canonicalPath, errorsPath, warningsPath, ct);
            }
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

    private async Task RunFullExtractionAsync(
        int bookId,
        DndMcpAICsharpFun.Domain.IngestionRecord record,
        string bookSlug,
        List<EntityCandidate> candidates,
        Dictionary<EntityType, JsonElement> schemas,
        string canonicalPath,
        string errorsPath,
        string warningsPath,
        CancellationToken ct)
    {
        var checkpointPath       = Path.Combine(_opts.CanonicalDirectory, bookSlug + ".progress.json");
        var checkpointErrorsPath = Path.Combine(_opts.CanonicalDirectory, bookSlug + ".progress.errors.json");

        var (extracted, extractionErrors, doneIds) =
            await checkpointStore.LoadCheckpointAsync(checkpointPath, checkpointErrorsPath);

        if (doneIds.Count > 0)
            logger.LogInformation(
                "Resuming from checkpoint: {Done} candidates already processed ({Extracted} ok, {Errors} failed)",
                doneIds.Count, extracted.Count, extractionErrors.Count);

        var (sourceBook, edition) = DeriveSourceAndEdition(record);
        bool isOfficial  = !string.IsNullOrWhiteSpace(record.FivetoolsSourceKey);
        var declined     = new List<DeclinedEntry>();
        var declinedPath = Path.Combine(_opts.CanonicalDirectory, bookSlug + ".declined.json");

        int success   = extracted.Count;
        int failed    = extractionErrors.Count;
        int processed = 0;
        var sw        = Stopwatch.StartNew();
        var lastLog   = TimeSpan.Zero;

        for (int i = 0; i < candidates.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var candidate = candidates[i];

            // Decline an official-book candidate whose PRIMARY prior type (TypePrior[0]) is gated and that did not match the 5etools index.
            // No LLM call; not counted as success or failure.
            var declineRes = DeterministicTypeResolver.Resolve(candidate, _matcher, isOfficial);
            if (declineRes.Outcome == DeterministicOutcome.Decline)
            {
                var rawId = EntityIdSlug.For(ExtractionEntityIds.BookKey(record), candidate.TypePrior.FirstOrDefault(), candidate.DisplayName);
                declined.Add(new DeclinedEntry(rawId, candidate.DisplayName, candidate.TypePrior.FirstOrDefault(), declineRes.DeclineReason ?? "no_5etools_match"));
                continue;
            }

            var id = ExtractionEntityIds.RecordedEntityId(record, candidate, _matcher, isOfficial);

            if (doneIds.Contains(id))
                continue;

            var (envelope, error) = await runner.ExtractOneAsync(record, candidate, id, sourceBook, edition, schemas, ct, isOfficial);

            if (error is not null)
            {
                extractionErrors.Add(error);
                failed++;
            }
            else
            {
                extracted.Add(envelope!);
                success++;
            }

            processed++;
            doneIds.Add(id);

            if (processed % _opts.CheckpointIntervalCandidates == 0)
                await checkpointStore.WriteCheckpointAsync(checkpointPath, checkpointErrorsPath, extracted, extractionErrors);

            if (sw.Elapsed - lastLog >= TimeSpan.FromSeconds(_opts.ProgressLogIntervalSeconds))
            {
                logger.LogInformation(
                    "Extraction progress: {Done}/{Total} ({Success} ok, {Failed} failed)",
                    success + failed, candidates.Count, success, failed);
                lastLog = sw.Elapsed;
            }
        }

        // Resolve references; classify intra vs inter book.
        var refWarnings = refResolver.Resolve(extracted).ToList();
        var classifier  = new IntraBookReferenceClassifier(bookSlug);
        var (intra, inter) = classifier.Partition(refWarnings);

        // Drop entities with intra-book dangling refs; record as errors.
        if (intra.Count > 0)
        {
            var offenders = intra.Select(w => w.SourceEntityId).ToHashSet(StringComparer.Ordinal);
            extracted.RemoveAll(e => offenders.Contains(e.Id));

            foreach (var w in intra)
            {
                extractionErrors.Add(new ExtractionErrorEntry(
                    SourceEntityId:  w.SourceEntityId,
                    FieldPath:       w.FieldPath,
                    MissingTargetId: w.MissingTargetId,
                    ErrorKind:       "intra_book_dangling_ref",
                    Detail:          null));
            }
        }

        var interWarnings = inter
            .Select(w => new ExtractionWarningEntry(
                SourceEntityId:  w.SourceEntityId,
                FieldPath:       w.FieldPath,
                MissingTargetId: w.MissingTargetId,
                WarningKind:     "inter_book_dangling_ref"))
            .ToList();

        // Write canonical JSON, errors, warnings.
        var canonicalFile = new CanonicalJsonFile(
            SchemaVersion: CanonicalJsonSchema.CurrentVersion,
            Book: new CanonicalBookMetadata(
                SourceBook:  sourceBook,
                Edition:     edition,
                FileHash:    record.FileHash,
                DisplayName: record.DisplayName),
            Entities: extracted);

        await writer.WriteAsync(canonicalPath, canonicalFile, ct);
        await errorsFile.WriteAsync(errorsPath, extractionErrors, ct);
        await warningsFile.WriteAsync(warningsPath, interWarnings, ct);
        await declinedFile.WriteAsync(declinedPath, declined, ct);

        // Update the database FIRST so that if checkpoint deletion fails, the record is already consistent.
        await tracker.MarkEntitiesExtractedAsync(bookId, extracted.Count, ct);

        // Remove checkpoint files now that the final output is written.
        try { File.Delete(checkpointPath); }
        catch (Exception ex) { logger.LogWarning(ex, "Could not delete checkpoint file {Path}", checkpointPath); }

        try { File.Delete(checkpointErrorsPath); }
        catch (Exception ex) { logger.LogWarning(ex, "Could not delete checkpoint errors file {Path}", checkpointErrorsPath); }

        logger.LogInformation(
            "Entity extraction complete: book {BookId}, {Clean} clean / {Errors} errors / {Warnings} warnings",
            bookId, extracted.Count, extractionErrors.Count, interWarnings.Count);
    }

    private async Task RunErrorsOnlyAsync(
        int bookId,
        DndMcpAICsharpFun.Domain.IngestionRecord record,
        string bookSlug,
        List<EntityCandidate> candidates,
        Dictionary<EntityType, JsonElement> schemas,
        string canonicalPath,
        string errorsPath,
        string warningsPath,
        CancellationToken ct)
    {
        // Pre-condition: canonical must already exist.
        CanonicalJsonFile existingFile;
        try
        {
            await using var cs = File.OpenRead(canonicalPath);
            existingFile = await JsonSerializer.DeserializeAsync<CanonicalJsonFile>(cs, ExtractionCheckpointStore.Options, ct)
                ?? throw new InvalidOperationException($"Canonical JSON at {canonicalPath} deserialised to null");
        }
        catch (FileNotFoundException)
        {
            throw new InvalidOperationException(
                $"No canonical JSON found for {bookSlug}; run full extraction first.");
        }

        // Load errors file → retrySet.
        List<ExtractionErrorEntry> previousErrors;
        try
        {
            await using var es = File.OpenRead(errorsPath);
            previousErrors = await JsonSerializer.DeserializeAsync<List<ExtractionErrorEntry>>(es, ExtractionCheckpointStore.Options, ct) ?? [];
        }
        catch (FileNotFoundException)
        {
            logger.LogInformation("No errors file found for {BookSlug}; nothing to retry", bookSlug);
            await tracker.MarkEntitiesExtractedAsync(bookId, existingFile.Entities.Count, ct);
            return;
        }

        if (previousErrors.Count == 0)
        {
            logger.LogInformation("Errors file for {BookSlug} is empty; nothing to retry", bookSlug);
            await tracker.MarkEntitiesExtractedAsync(bookId, existingFile.Entities.Count, ct);
            return;
        }

        var retrySet = previousErrors.Select(e => e.SourceEntityId).ToHashSet(StringComparer.Ordinal);
        logger.LogInformation(
            "Re-extracting {Count} failed entities for book {BookId}", retrySet.Count, bookId);

        var newlyExtracted = new List<EntityEnvelope>();
        var newErrors      = new List<ExtractionErrorEntry>();

        var (sourceBook, edition) = DeriveSourceAndEdition(record);
        bool isOfficial = !string.IsNullOrWhiteSpace(record.FivetoolsSourceKey);

        for (int i = 0; i < candidates.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var candidate = candidates[i];

            // If the candidate is now declined (e.g. allowlist updated since full run), skip silently.
            if (DeterministicTypeResolver.Resolve(candidate, _matcher, isOfficial).Outcome == DeterministicOutcome.Decline)
                continue;

            var id = ExtractionEntityIds.RecordedEntityId(record, candidate, _matcher, isOfficial);

            if (!retrySet.Contains(id)) continue;

            var (envelope, error) = await runner.ExtractOneAsync(record, candidate, id, sourceBook, edition, schemas, ct, isOfficial);

            if (error is not null)
                newErrors.Add(error);
            else
                newlyExtracted.Add(envelope!);
        }

        // Reference resolution on newly extracted only.
        var refWarnings = refResolver.Resolve(newlyExtracted).ToList();
        var classifier  = new IntraBookReferenceClassifier(bookSlug);
        var (intra, inter) = classifier.Partition(refWarnings);

        if (intra.Count > 0)
        {
            var offenders = intra.Select(w => w.SourceEntityId).ToHashSet(StringComparer.Ordinal);
            newlyExtracted.RemoveAll(e => offenders.Contains(e.Id));
            foreach (var w in intra)
                newErrors.Add(new ExtractionErrorEntry(
                    SourceEntityId:  w.SourceEntityId,
                    FieldPath:       w.FieldPath,
                    MissingTargetId: w.MissingTargetId,
                    ErrorKind:       "intra_book_dangling_ref",
                    Detail:          null));
        }

        var interWarnings = inter
            .Select(w => new ExtractionWarningEntry(
                SourceEntityId:  w.SourceEntityId,
                FieldPath:       w.FieldPath,
                MissingTargetId: w.MissingTargetId,
                WarningKind:     "inter_book_dangling_ref"))
            .ToList();

        // Load pre-existing inter-book warnings; drop entries for retried entities (may now be resolved).
        List<ExtractionWarningEntry> previousWarnings;
        try
        {
            await using var ws = File.OpenRead(warningsPath);
            previousWarnings = await JsonSerializer.DeserializeAsync<List<ExtractionWarningEntry>>(ws, ExtractionCheckpointStore.Options, ct) ?? [];
        }
        catch (FileNotFoundException)
        {
            previousWarnings = [];
        }

        var mergedWarnings = previousWarnings
            .Where(w => !retrySet.Contains(w.SourceEntityId))
            .Concat(interWarnings)
            .ToList();

        // Merge newly extracted entities into the existing canonical file.
        var mergedEntities = existingFile.Entities.Concat(newlyExtracted).ToList();
        var mergedFile     = existingFile with { Entities = mergedEntities };

        await writer.WriteAsync(canonicalPath, mergedFile, ct);
        await errorsFile.WriteAsync(errorsPath, newErrors, ct);
        await warningsFile.WriteAsync(warningsPath, mergedWarnings, ct);

        logger.LogInformation(
            "Error re-extraction complete: book {BookId}, {New} newly extracted, {StillFailing} still failing, {Warnings} warnings",
            bookId, newlyExtracted.Count, newErrors.Count, mergedWarnings.Count);

        await tracker.MarkEntitiesExtractedAsync(bookId, mergedEntities.Count, ct);
    }

    private (string SourceBook, string Edition) DeriveSourceAndEdition(DndMcpAICsharpFun.Domain.IngestionRecord record)
    {
        var sourceBook = record.FivetoolsSourceKey ?? record.DisplayName;
        var edition    = record.FivetoolsSourceKey is { } key && registry.TryGetBook(key) is { } info
            ? (info.PublishedYear >= 2024 ? "Edition2024" : "Edition2014")
            : record.Version;
        return (sourceBook, edition);
    }
}
