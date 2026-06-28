using System.Diagnostics;
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed class EntityExtractionOrchestrator(
    IIngestionTracker tracker,
    BookSourceRegistry registry,
    IPdfStructureConverter converter,
    IPdfBookmarkReader bookmarks,
    EntityCandidateScanner scanner,
    StatBlockScanner statBlockScanner,
    CanonicalJsonWriter writer,
    ExtractionErrorsFile errorsFile,
    ExtractionWarningsFile warningsFile,
    ExtractionDeclinedFile declinedFile,
    EntityReferenceResolver refResolver,
    EntitySchemaProvider schemaProvider,
    ExtractionCheckpointStore checkpointStore,
    CandidateExtractor candidateExtractor,
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
            .For(BookKey(record), EntityType.Class, "x")
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

            // 1. Convert PDF via Marker (markdown + structured items).
            var doc = await converter.ConvertAsync(record.FilePath, ct);

            // 2. Read bookmarks → TocCategoryMap.
            var pdfBookmarks = bookmarks.ReadBookmarks(record.FilePath);
            var tocEntries   = BookmarkTocMapper.Map(pdfBookmarks);
            var tocMap       = new TocCategoryMap(tocEntries);

            // 2b. No embedded bookmarks → derive the TOC from Marker's heading structure items,
            // reusing the same deterministic keyword classifier (no LLM). Bookmarked books skip this.
            if (tocMap.IsEmpty)
            {
                var headingItems = doc.Items
                    .Where(i => string.Equals(i.Type, "section_header", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var headingEntries = HeadingTocMapper.Map(headingItems);
                tocMap = new TocCategoryMap(headingEntries);
                logger.LogInformation(
                    "No bookmarks for book {BookId}; derived TOC from {HeadingCount} headings → {EntryCount} confident category entries (heading-derived fallback)",
                    bookId, headingItems.Count, headingEntries.Count);
            }

            // 3. Project structure items into ScannerInputs.
            var scannerInputs = BuildScannerInputs(doc.Items);
            var sectionCandidates = scanner.Scan(scannerInputs, tocMap).ToList();
            // Recover stat blocks Marker failed to tag with a heading (headerless / fragmented under
            // mis-detected ACTIONS headers). Prepend so they win the id-keyed dedup with clean stat-block
            // text and supersede a headerless monster's lore-only section candidate.
            var statBlockCandidates = statBlockScanner.Scan(doc.Items).ToList();
            // Collapse same-id candidates (a header-clean monster yields both a section and a
            // stat-block candidate) to the best input: prefer the one carrying a stat block, then
            // the richer text — so header-clean monsters extract from full-context section text
            // (reliable) and headerless ones keep their stat-block candidate.
            var candidates    = ExtractionCandidateDeduplicator.Dedupe(
                statBlockCandidates.Concat(sectionCandidates), BookKey(record));

            // Deterministic resolution drops non-entity-named candidates (headings/fragments) before
            // extraction — no wasted LLM call, no garbage entity. INVARIANT: this prefilter omits
            // isOfficial on purpose — it removes ONLY Drop. Declines must survive to the recording loop
            // below (which passes isOfficial) so they reach declined.json; passing isOfficial here would
            // silently filter them out. Do not "fix" it to pass isOfficial.
            var keptCandidates = candidates
                .Where(c => DeterministicTypeResolver.Resolve(c, _matcher).Outcome != DeterministicOutcome.Drop)
                .ToList();
            var droppedCount = candidates.Count - keptCandidates.Count;
            if (droppedCount > 0)
                logger.LogInformation("Dropped {Count} non-entity-named candidates before extraction", droppedCount);
            candidates = keptCandidates;

            logger.LogInformation(
                "Entity extraction: {CandidateCount} candidates from {ItemCount} structure items",
                candidates.Count, doc.Items.Count);

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
                var rawId = EntityIdSlug.For(BookKey(record), candidate.TypePrior.FirstOrDefault(), candidate.DisplayName);
                declined.Add(new DeclinedEntry(rawId, candidate.DisplayName, candidate.TypePrior.FirstOrDefault(), declineRes.DeclineReason ?? "no_5etools_match"));
                continue;
            }

            var id = RecordedEntityId(record, candidate, isOfficial);

            if (doneIds.Contains(id))
                continue;

            var (envelope, error) = await ExtractOneAsync(record, candidate, id, sourceBook, edition, schemas, ct, isOfficial);

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

            var id = RecordedEntityId(record, candidate, isOfficial);

            if (!retrySet.Contains(id)) continue;

            var (envelope, error) = await ExtractOneAsync(record, candidate, id, sourceBook, edition, schemas, ct, isOfficial);

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

    // The book identifier used for slug/id derivation: the 5etools source key when present
    // (mapped to a canonical slug like phb14 by EntityIdSlug), else the display name.
    private static string BookKey(DndMcpAICsharpFun.Domain.IngestionRecord record) =>
        record.FivetoolsSourceKey ?? record.DisplayName;

    private (string SourceBook, string Edition) DeriveSourceAndEdition(DndMcpAICsharpFun.Domain.IngestionRecord record)
    {
        var sourceBook = record.FivetoolsSourceKey ?? record.DisplayName;
        var edition    = record.FivetoolsSourceKey is { } key && registry.TryGetBook(key) is { } info
            ? (info.PublishedYear >= 2024 ? "Edition2024" : "Edition2014")
            : record.Version;
        return (sourceBook, edition);
    }

    /// <summary>
    /// Shared per-candidate extraction pipeline used by both full and errors-only run modes.
    /// Returns either a successfully built <see cref="EntityEnvelope"/> or an <see cref="ExtractionErrorEntry"/>;
    /// exactly one of the two tuple members is non-null.
    /// </summary>
    // The id under which an entity (or its error) is recorded: the canonical 5etools id when the
    // name matches the index, else the raw heading id. Every membership test (checkpoint doneIds,
    // errors-only retrySet) and ExtractOneAsync must agree on this id, or matched candidates are
    // silently re-extracted (resume) or skipped (retry).
    private string RecordedEntityId(DndMcpAICsharpFun.Domain.IngestionRecord record, EntityCandidate candidate, bool isOfficial = false)
    {
        var resolution = DeterministicTypeResolver.Resolve(candidate, _matcher, isOfficial);
        return resolution.Outcome == DeterministicOutcome.ForceType && resolution.CanonicalName is { } cn
            ? EntityIdSlug.For(BookKey(record), resolution.ForcedType, cn)
            : EntityIdSlug.For(BookKey(record), candidate.Type, candidate.DisplayName);
    }

    private async Task<(EntityEnvelope? Envelope, ExtractionErrorEntry? Error)> ExtractOneAsync(
        DndMcpAICsharpFun.Domain.IngestionRecord record,
        EntityCandidate candidate,
        string id,
        string sourceBook,
        string edition,
        Dictionary<EntityType, JsonElement> schemas,
        CancellationToken ct,
        bool isOfficial = false)
    {
        // Offer only prior types that actually have a schema. If none do, it is a configuration
        // problem (no_schema), recorded without an LLM call.
        var availablePrior = candidate.TypePrior.Where(schemas.ContainsKey).ToList();
        if (availablePrior.Count == 0)
        {
            logger.LogWarning(
                "No schema for any prior type of candidate {Name}; recording no_schema",
                candidate.DisplayName);
            return (null, new ExtractionErrorEntry(
                SourceEntityId: id,
                FieldPath: "(type)",
                MissingTargetId: string.Empty,
                ErrorKind: "no_schema",
                Detail: $"No JSON schema for any prior type of {candidate.DisplayName}"));
        }

        var displayName = NormalizeDisplayName(candidate.DisplayName);

        // Deterministic type resolution before the content-first union. Drop/Decline candidates are
        // handled upstream; only ForceType/Defer reach here. A forced type extracts with that
        // type's schema directly; Defer uses the union below.
        var resolution = DeterministicTypeResolver.Resolve(candidate, _matcher, isOfficial);

        // When the 5etools matcher supplies a canonical name, use it for both the entity's
        // display name and ID so the canonical JSON reflects the authoritative 5etools spelling
        // rather than the raw (often all-caps) heading text.
        if (resolution.Outcome == DeterministicOutcome.ForceType && resolution.CanonicalName is { } cn)
        {
            displayName = NormalizeDisplayName(cn);
            id = EntityIdSlug.For(BookKey(record), resolution.ForcedType, cn);
        }

        if (resolution.Outcome == DeterministicOutcome.ForceType &&
            schemas.TryGetValue(resolution.ForcedType, out var forcedSchema))
        {
            var (forcedFields, forcedError) = await candidateExtractor.ExtractFieldsAsync(
                record, candidate with { Type = resolution.ForcedType }, forcedSchema, ct);
            if (forcedFields is null)
            {
                logger.LogWarning(
                    "Forced {Type} extraction failed for '{Name}' (page {Page}): {Error}",
                    resolution.ForcedType, candidate.DisplayName, candidate.Page, forcedError);
                return (null, new ExtractionErrorEntry(
                    SourceEntityId: id, FieldPath: "(extraction)", MissingTargetId: string.Empty,
                    ErrorKind: "extraction_failure", Detail: forcedError));
            }

            var forcedConfidence = forcedFields.Value.TryGetProperty("confidence", out var fcp) ? fcp.GetString() : null;
            var forcedClean = CandidateExtractor.StripConfidence(forcedFields.Value);
            return (BuildTypedEnvelope(id, resolution.ForcedType, displayName, sourceBook, edition, candidate, forcedClean, forcedConfidence), null);
        }

        var result = await candidateExtractor.ExtractUnionAsync(record, candidate, availablePrior, schemas, ct);

        switch (result.Outcome)
        {
            case UnionOutcome.Failed:
                logger.LogWarning(
                    "Union extraction failed for '{Name}' (page {Page}): {Error}",
                    candidate.DisplayName, candidate.Page, result.ErrorMessage);
                return (null, new ExtractionErrorEntry(
                    SourceEntityId: id,
                    FieldPath: "(extraction)",
                    MissingTargetId: string.Empty,
                    ErrorKind: "extraction_failure",
                    Detail: result.ErrorMessage));

            case UnionOutcome.Declined:
                return (DeclinedEnvelope(id, candidate, displayName, sourceBook, edition, result.DeclineReason), null);

            default:
                return (BuildTypedEnvelope(id, result.Type, displayName, sourceBook, edition, candidate, result.Fields, result.Confidence), null);
        }
    }

    // Builds a typed entity envelope, deriving the disposition from grounding + name/confidence.
    // The Id keeps the keyword-primary type for stable checkpoint/resume identity (design.md §F);
    // the authoritative type is the Type field.
    private EntityEnvelope BuildTypedEnvelope(
        string id, EntityType type, string displayName, string sourceBook, string edition,
        EntityCandidate candidate, JsonElement fields, string? confidence)
    {
        var grounded = HasGroundedContent(fields, candidate.Text);
        var disposition = ExtractionDispositionPolicy.Derive(grounded, displayName, confidence);
        return new EntityEnvelope(
            Id:              id,
            Type:            type,
            Name:            displayName,
            SourceBook:      sourceBook,
            Edition:         edition,
            Page:            candidate.Page,
            FirstAppearedIn: new FirstAppearance(sourceBook, edition, candidate.Page),
            RevisedIn:       Array.Empty<Revision>(),
            SettingTags:     Array.Empty<string>(),
            CanonicalText:   string.Empty,
            Fields:          fields,
            NeedsReview:     disposition != EntityDisposition.Accepted,
            Disposition:     disposition);
    }

    private static EntityEnvelope DeclinedEnvelope(
        string id, EntityCandidate candidate, string displayName,
        string sourceBook, string edition, string? reason)
    {
        using var empty = JsonDocument.Parse("{}");
        return new EntityEnvelope(
            Id:              id,
            Type:            candidate.Type,
            Name:            displayName,
            SourceBook:      sourceBook,
            Edition:         edition,
            Page:            candidate.Page,
            FirstAppearedIn: new FirstAppearance(sourceBook, edition, candidate.Page),
            RevisedIn:       Array.Empty<Revision>(),
            SettingTags:     Array.Empty<string>(),
            CanonicalText:   reason ?? string.Empty,
            Fields:          empty.RootElement.Clone(),
            NeedsReview:     true,
            Disposition:     EntityDisposition.Declined);
    }

    // Tier-0 grounding over the emitted fields: true when at least one significant string value
    // grounds against the source prose. Pure fabrication / empty output (e.g. zeroed stat blocks)
    // grounds nothing -> NeedsReview.
    private static bool HasGroundedContent(JsonElement fields, string sourceText)
    {
        foreach (var value in EnumerateStringValues(fields))
            if (Tier0FieldGrounding.IsTextGrounded(value, sourceText))
                return true;
        return false;
    }

    private static IEnumerable<string> EnumerateStringValues(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var s = element.GetString();
                if (!string.IsNullOrWhiteSpace(s)) yield return s;
                break;
            case JsonValueKind.Object:
                foreach (var p in element.EnumerateObject())
                    foreach (var v in EnumerateStringValues(p.Value))
                        yield return v;
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    foreach (var v in EnumerateStringValues(item))
                        yield return v;
                break;
        }
    }

    private static IList<ScannerInput> BuildScannerInputs(IReadOnlyList<PdfStructureItem> items)
    {
        var inputs = new List<ScannerInput>(items.Count);
        var currentSection = "(unknown)";
        foreach (var item in items)
        {
            var type = item.Type ?? string.Empty;
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

    // Title-case clean all-caps display names before they become entity names + feed the heuristic.
    private static string NormalizeDisplayName(string displayName)
        => EntityNameNormalizer.TryNormalizeHeading(displayName, out var n) ? n : displayName;
}
