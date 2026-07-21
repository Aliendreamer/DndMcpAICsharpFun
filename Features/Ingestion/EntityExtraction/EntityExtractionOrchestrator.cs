using System.Diagnostics;
using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;

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
    EntityFieldFillService fieldFill,
    IOptions<EntityExtractionOptions> options,
    ILogger<EntityExtractionOrchestrator> logger,
    EntityNameMatcher? matcher = null,
    DeclineRecovery? declineRecovery = null) : IEntityExtractionOrchestrator
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
        var errorsPath = Path.Combine(_opts.CanonicalDirectory, bookSlug + ".errors.json");
        var warningsPath = Path.Combine(_opts.CanonicalDirectory, bookSlug + ".warnings.json");

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

            // 4b. mineru-table-extraction: parse MinerU-preserved tables into CanonicalTables
            // (deterministic; independent of the LLM entity path). Errors-only reuses the existing
            // canonical file, so only the full path re-writes Tables.
            var tables = MinerUTableCollector.Collect(
                doc, bookSlug, record.FivetoolsSourceKey ?? record.DisplayName);

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
                    canonicalPath, errorsPath, warningsPath, tables, ct);
            }

            // 6. Field-fill: enrich the just-written canonical with allowlisted 5etools fields
            // before ingest-entities. Deterministic (fill-missing-only merge over the same
            // 5etools roster), so a force re-extract re-derives the same fill. Extraction has
            // already succeeded at this point, so a fill failure must be logged and swallowed —
            // never allowed to fail the extraction.
            try
            {
                var fillResult = await fieldFill.FillAsync(record, ct);
                logger.LogInformation(
                    "Entity field-fill after extraction: book {BookId}, hasSourceKey={HasSourceKey}, " +
                    "entitiesTouched={EntitiesTouched}, filledByType={FilledByType}",
                    bookId,
                    fillResult.HasSourceKey,
                    fillResult.EntitiesTouched,
                    string.Join(", ", fillResult.FilledByType.Select(kv => $"{kv.Key}={kv.Value}")));
            }
            catch (Exception fillEx)
            {
                logger.LogError(
                    fillEx,
                    "Entity field-fill failed for book {BookId} after successful extraction; canonical is unfilled but extraction succeeded",
                    bookId);
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
        IReadOnlyList<CanonicalTable> tables,
        CancellationToken ct)
    {
        var checkpointPath = Path.Combine(_opts.CanonicalDirectory, bookSlug + ".progress.json");
        var checkpointErrorsPath = Path.Combine(_opts.CanonicalDirectory, bookSlug + ".progress.errors.json");

        var (extracted, extractionErrors, doneIds) =
            await checkpointStore.LoadCheckpointAsync(checkpointPath, checkpointErrorsPath);

        if (doneIds.Count > 0)
            logger.LogInformation(
                "Resuming from checkpoint: {Done} candidates already processed ({Extracted} ok, {Errors} failed)",
                doneIds.Count, extracted.Count, extractionErrors.Count);

        var (sourceBook, edition) = DeriveSourceAndEdition(record);
        bool isOfficial = !string.IsNullOrWhiteSpace(record.FivetoolsSourceKey);
        var declined = new List<DeclinedEntry>();
        // Candidates declined either deterministically (below) or by the LLM's "none" union pick
        // (extraction_declined) — replayed post-loop through DeclineRecovery for official books.
        var declinedCandidates = new List<EntityCandidate>();
        var declinedPath = Path.Combine(_opts.CanonicalDirectory, bookSlug + ".declined.json");

        int success = extracted.Count;
        int failed = extractionErrors.Count;
        int processed = 0;
        var sw = Stopwatch.StartNew();
        var lastLog = TimeSpan.Zero;

        for (int i = 0; i < candidates.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var candidate = candidates[i];

            // Decline an official-book candidate whose PRIMARY prior type (TypePrior[0]) is gated and that did not match the 5etools index.
            // No LLM call; not counted as success or failure.
            var declineRes = DeterministicTypeResolver.Resolve(candidate, _matcher, isOfficial);
            if (declineRes.Outcome == DeterministicOutcome.Decline)
            {
                var rescued = RescueAsItemOrNull(candidate, declineRes.Outcome)
                           ?? RescueAsRuleOrNull(candidate, declineRes.Outcome);
                if (rescued is null)
                {
                    var rawId = EntityIdSlug.For(ExtractionEntityIds.BookKey(record), candidate.TypePrior.FirstOrDefault(), candidate.DisplayName);
                    declined.Add(new DeclinedEntry(rawId, candidate.DisplayName, candidate.TypePrior.FirstOrDefault(), declineRes.DeclineReason ?? "no_5etools_match"));
                    declinedCandidates.Add(candidate);
                    continue;
                }
                candidate = rescued;
            }

            var id = ExtractionEntityIds.RecordedEntityId(record, candidate, _matcher, isOfficial);

            if (doneIds.Contains(id))
                continue;

            var (envelope, error) = await runner.ExtractOneAsync(record, candidate, id, sourceBook, edition, schemas, ct, isOfficial);

            if (error is not null)
            {
                extractionErrors.Add(error);
                failed++;
                if (error.ErrorKind == "extraction_declined")
                    declinedCandidates.Add(candidate);
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

        // Automatic decline-recovery (official books only): replay every candidate declined above —
        // deterministically or via the LLM's "none" pick — rebound to the Rule/Lore union. Only a
        // grounded Accepted pick is admitted; everything else stays declined (anti-fabrication).
        // Runs BEFORE reference resolution so a recovered entity's refs are checked like any other.
        if (isOfficial && declineRecovery is not null)
        {
            var recoveredCount = 0;
            foreach (var declinedCandidate in declinedCandidates)
            {
                // The recovered envelope's Id is honest (derived from its ACTUAL disposed type, e.g.
                // Rule/Lore — see DeclineRecovery.TryRecoverAsync), so it no longer matches the id the
                // candidate was ORIGINALLY declined/errored under (its pre-recovery, original-type
                // id). Reconciliation must therefore match on that original id, computed here from the
                // still-original-typed candidate, before recovery rebinds it.
                var originalId = ExtractionEntityIds.RecordedEntityId(record, declinedCandidate, _matcher, isOfficial: true);

                var rec = await declineRecovery.TryRecoverAsync(record, declinedCandidate, sourceBook, edition, schemas, ct);
                if (rec is null) continue;

                extracted.Add(rec);
                recoveredCount++;
                var removedFromDeclined = declined.RemoveAll(d => d.Id == originalId);
                var removedFromErrors = extractionErrors.RemoveAll(e => e.ErrorKind == "extraction_declined" && e.SourceEntityId == originalId);

                // A recovered entity should reconcile out of exactly one audit trail: the declined
                // list (deterministic decline) or the errors sidecar (LLM "none" decline) — never
                // neither. Zero on BOTH means originalId didn't match the id the candidate was
                // originally recorded under, which would silently leave a stale declined/error entry
                // alongside the now-admitted entity (e.g. if a future candidate breaks the
                // TypePrior[0]==Type identity convention that RecordedEntityId relies on).
                if (removedFromDeclined == 0 && removedFromErrors == 0)
                    logger.LogWarning(
                        "Decline-recovery admitted entity {Id} (original id {OriginalId}) but reconciled no declined-audit or error entry for it",
                        rec.Id, originalId);
            }

            if (recoveredCount > 0)
                logger.LogInformation("Decline-recovery admitted {Recovered} Rule/Lore entities", recoveredCount);
        }

        // Resolve references; classify intra vs inter book.
        var refWarnings = refResolver.Resolve(extracted).ToList();
        var classifier = new IntraBookReferenceClassifier(bookSlug);
        var (intra, inter) = classifier.Partition(refWarnings);

        // Drop entities with intra-book dangling refs; record as errors.
        if (intra.Count > 0)
        {
            var offenders = intra.Select(w => w.SourceEntityId).ToHashSet(StringComparer.Ordinal);
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

        // Write canonical JSON, errors, warnings.
        var canonicalFile = new CanonicalJsonFile(
            SchemaVersion: CanonicalJsonSchema.CurrentVersion,
            Book: new CanonicalBookMetadata(
                SourceBook: sourceBook,
                Edition: edition,
                FileHash: record.FileHash,
                DisplayName: record.DisplayName),
            Entities: extracted,
            Tables: tables);

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
        var newErrors = new List<ExtractionErrorEntry>();

        var (sourceBook, edition) = DeriveSourceAndEdition(record);
        bool isOfficial = !string.IsNullOrWhiteSpace(record.FivetoolsSourceKey);

        for (int i = 0; i < candidates.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var candidate = candidates[i];

            // If the candidate is now declined (e.g. allowlist updated since full run), skip silently.
            var errDecline = DeterministicTypeResolver.Resolve(candidate, _matcher, isOfficial);
            if (errDecline.Outcome == DeterministicOutcome.Decline)
            {
                var rescued = RescueAsItemOrNull(candidate, errDecline.Outcome)
                           ?? RescueAsRuleOrNull(candidate, errDecline.Outcome);
                if (rescued is null) continue;
                candidate = rescued;
            }

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
        var classifier = new IntraBookReferenceClassifier(bookSlug);
        var (intra, inter) = classifier.Partition(refWarnings);

        if (intra.Count > 0)
        {
            var offenders = intra.Select(w => w.SourceEntityId).ToHashSet(StringComparer.Ordinal);
            newlyExtracted.RemoveAll(e => offenders.Contains(e.Id));
            foreach (var w in intra)
                newErrors.Add(new ExtractionErrorEntry(
                    SourceEntityId: w.SourceEntityId,
                    FieldPath: w.FieldPath,
                    MissingTargetId: w.MissingTargetId,
                    ErrorKind: "intra_book_dangling_ref",
                    Detail: null));
        }

        var interWarnings = inter
            .Select(w => new ExtractionWarningEntry(
                SourceEntityId: w.SourceEntityId,
                FieldPath: w.FieldPath,
                MissingTargetId: w.MissingTargetId,
                WarningKind: "inter_book_dangling_ref"))
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
        var mergedFile = existingFile with { Entities = mergedEntities };

        await writer.WriteAsync(canonicalPath, mergedFile, ct);
        await errorsFile.WriteAsync(errorsPath, newErrors, ct);
        await warningsFile.WriteAsync(warningsPath, mergedWarnings, ct);

        logger.LogInformation(
            "Error re-extraction complete: book {BookId}, {New} newly extracted, {StillFailing} still failing, {Warnings} warnings",
            bookId, newlyExtracted.Count, newErrors.Count, mergedWarnings.Count);

        await tracker.MarkEntitiesExtractedAsync(bookId, mergedEntities.Count, ct);
    }

    // internal (not private) so DndMcpAICsharpFun.Tests can exercise this pure logic directly
    // (InternalsVisibleTo is already configured).
    // Rescue a would-be-declined candidate that carries a mundane weapon/armor stat signature as an
    // Item — checked BEFORE the Rule rescue so a genuine item is never mis-typed Rule. TypePrior is
    // replaced with [Item] only (the failed gated type is not re-offered).
    internal static EntityCandidate? RescueAsItemOrNull(EntityCandidate candidate, DeterministicOutcome outcome) =>
        outcome == DeterministicOutcome.Decline && ExtractionSignatures.ItemSignature(candidate)
            ? candidate with { TypePrior = new[] { EntityType.Item } }
            : null;

    // Rescue a would-be-declined candidate that reads as a rule: re-offer it as Rule-or-none
    // (TypePrior swapped to [Rule] only — the failed gated type is NOT re-offered, so the LLM
    // cannot fabricate an ungrounded canon entity). Returns the rebound candidate, or null to decline.
    internal static EntityCandidate? RescueAsRuleOrNull(EntityCandidate candidate, DeterministicOutcome outcome) =>
        outcome == DeterministicOutcome.Decline && ExtractionSignatures.RuleSignature(candidate)
            ? candidate with { TypePrior = new[] { EntityType.Rule } }
            : null;

    private (string SourceBook, string Edition) DeriveSourceAndEdition(DndMcpAICsharpFun.Domain.IngestionRecord record)
    {
        var sourceBook = record.FivetoolsSourceKey ?? record.DisplayName;
        var edition = record.FivetoolsSourceKey is { } key && registry.TryGetBook(key) is { } info
            ? (info.PublishedYear >= 2024 ? "Edition2024" : "Edition2014")
            : record.Version;
        return (sourceBook, edition);
    }
}