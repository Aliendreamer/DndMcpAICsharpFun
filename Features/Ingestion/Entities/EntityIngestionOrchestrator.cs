using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.VectorStore.Entities;

using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Ingestion.Entities;

public sealed class EntityIngestionOptions
{
    // Must match EntityExtractionOptions.CanonicalDirectory — extraction writes and ingestion reads
    // the same canonical files. Overridden to "/books/canonical" in the container via the
    // EntityIngestion config section.
    public string CanonicalDirectory { get; set; } = "books/canonical";

    /// <summary>
    /// Root directory of the local 5etools data checkout.
    /// Used to build the enrichment index at ingest time.
    /// Defaults to "5etools" (relative to working dir). Override in container via
    /// <c>EntityIngestion__FivetoolsDirectory</c> env var.
    /// If the directory is absent, enrichment is silently skipped.
    /// </summary>
    public string FivetoolsDirectory { get; set; } = "5etools";
}

public sealed class EntityIngestionOrchestrator(
    IIngestionTracker tracker,
    CanonicalJsonLoader loader,
    EntityCanonicalTextDispatcher textDispatcher,
    EntityReferenceResolver refResolver,
    IEmbeddingService embeddings,
    IEntityVectorStore store,
    IOptions<EntityIngestionOptions> options,
    ILogger<EntityIngestionOrchestrator> logger) : IEntityIngestionOrchestrator
{
    private readonly EntityIngestionOptions _opts = options.Value;

    public async Task<EntityIngestionResult> IngestEntitiesAsync(int bookId, CancellationToken ct = default)
    {
        var record = await tracker.GetByIdAsync(bookId, ct)
                     ?? throw new InvalidOperationException($"No ingestion record {bookId}");

        var bookSlug = EntityIdSlug.BookSlug(record);
        var path = Path.Combine(_opts.CanonicalDirectory, bookSlug + ".json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Canonical JSON not found for book {bookId} at {path}", path);

        var file = await loader.LoadAsync(path, ct);

        foreach (var w in refResolver.Resolve(file.Entities))
            logger.LogWarning("Dangling entity reference: {Source} -> {Target} ({Path})",
                w.SourceEntityId, w.MissingTargetId, w.FieldPath);

        // ── 5etools enrichment index ──────────────────────────────────────────
        // Build an in-memory id → envelope map from the local 5etools files, filtered to
        // the source key(s) of the book being ingested (fast, no Qdrant).
        // If the 5etools directory is absent the index is empty and we proceed unenriched.
        var fivetoolsIndex = await BuildFivetoolsIndexAsync(
            record, "Could not build 5etools enrichment index — proceeding unenriched", ct);

        if (fivetoolsIndex.Count == 0)
            logger.LogInformation(
                "5etools enrichment skipped for book {BookId}: index is empty (files absent or unmatched source key)",
                bookId);

        // ── Pre-fetch existing Qdrant data ────────────────────────────────────
        // Used for the existing "don't clobber manual / preserve prior" behaviour.
        // The 5etools file record takes priority over the Qdrant store record when BOTH
        // exist for the same id (file record is more authoritative for enrichment).
        var entityIds = file.Entities.Select(e => e.Id).ToList();
        var existingQdrant = await store.GetByIdsAsync(entityIds, ct);

        // ── Merge + render ────────────────────────────────────────────────────
        var renderedEnvelopes = new List<EntityEnvelope>(file.Entities.Count);
        var texts = new List<string>(file.Entities.Count);
        int matchedFivetools = 0;
        int unmatched = 0;

        foreach (var envelope in file.Entities)
        {
            ct.ThrowIfCancellationRequested();

            // Ungrounded entities are fabrications (Task: entity-grounding-cascade) — never
            // (re-)add them to dnd_entities, even on a full re-ingest of the book.
            if (envelope.Disposition == EntityDisposition.Ungrounded)
                continue;

            var merged = MergeEnrichment(envelope, fivetoolsIndex, existingQdrant, out var matchedThis);
            if (matchedThis) matchedFivetools++; else unmatched++;

            string text;
            try
            {
                text = textDispatcher.Render(merged);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Skipping entity {Id} — render failed", merged.Id);
                continue;
            }

            renderedEnvelopes.Add(WithRenderedText(merged, text, record));
            texts.Add(text);
        }

        // ── Embed + upsert ────────────────────────────────────────────────────
        IList<float[]> vectors = texts.Count == 0
            ? Array.Empty<float[]>()
            : await embeddings.EmbedAsync(texts, ct);

        var points = new List<EntityPoint>(renderedEnvelopes.Count);
        for (int i = 0; i < renderedEnvelopes.Count; i++)
            points.Add(new EntityPoint(renderedEnvelopes[i], vectors[i], record.FileHash));

        // Upsert first — if this fails, the old vectors are still intact.
        await store.UpsertAsync(points, ct);

        // Remove only STALE vectors from a prior extraction of this book — points sharing this
        // book's FileHash whose entity is no longer produced. The just-upserted entities are kept
        // (their ids are passed), so the batch we just wrote is never deleted.
        // Guard: skip if FileHash is empty (book registered but never block-ingested).
        if (!string.IsNullOrEmpty(record.FileHash))
            await store.DeleteByFileHashExceptAsync(
                record.FileHash, renderedEnvelopes.Select(e => e.Id).ToList(), ct);

        await tracker.MarkEntitiesIngestedAsync(bookId, points.Count, ct);
        logger.LogInformation(
            "Entity ingestion complete: book {BookId}, {Count} entities " +
            "(matched5etools={Matched}, unmatched={Unmatched})",
            bookId, points.Count, matchedFivetools, unmatched);

        return new EntityIngestionResult(
            TotalEntities: points.Count,
            MatchedFivetools: matchedFivetools,
            Unmatched: unmatched);
    }

    /// <inheritdoc/>
    /// <inheritdoc/>
    public async Task ReindexEntityAsync(int bookId, string entityId, CancellationToken ct = default)
    {
        var record = await tracker.GetByIdAsync(bookId, ct)
                     ?? throw new InvalidOperationException($"No ingestion record {bookId}");

        var bookSlug = EntityIdSlug.BookSlug(record);
        var path = Path.Combine(_opts.CanonicalDirectory, bookSlug + ".json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Canonical JSON not found for book {bookId} at {path}", path);

        var file = await loader.LoadAsync(path, ct);
        var envelope = file.Entities.FirstOrDefault(e =>
            string.Equals(e.Id, entityId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Entity '{entityId}' not found in {path}");

        // entity-grounding-cascade Fix I-1(c): a judge-confirmed fabrication must never be
        // (re-)indexed into dnd_entities, even via the accept/edit/bulk-accept reindex path — the
        // reground pass leaves NeedsReview=true on Ungrounded entities so they stay in the review
        // queue, and an admin accepting/bulk-accepting one clears NeedsReview without changing the
        // Disposition. Delete instead of upsert so the invariant holds regardless of caller.
        if (envelope.Disposition == EntityDisposition.Ungrounded)
        {
            await store.DeleteByIdsAsync([entityId], ct);
            logger.LogWarning(
                "Reindex of Ungrounded entity {EntityId} (book {BookId}) converted to a delete — " +
                "fabrications are never (re-)indexed into dnd_entities",
                entityId, bookId);
            return;
        }

        // ── Enrichment (same path as IngestEntitiesAsync but for a single entity) ──
        var fivetoolsIndex = await BuildFivetoolsIndexAsync(
            record, "Could not build 5etools enrichment index for single-entity reindex — proceeding unenriched", ct);

        // Only fetch the existing Qdrant point when there's no 5etools match — mirrors the
        // original lazy fetch (the 5etools file record always takes priority when both exist).
        IReadOnlyDictionary<string, EntityEnvelope> existingQdrant = fivetoolsIndex.ContainsKey(envelope.Id)
            ? new Dictionary<string, EntityEnvelope>()
            : await store.GetByIdsAsync([envelope.Id], ct);

        var merged = MergeEnrichment(envelope, fivetoolsIndex, existingQdrant, out _);

        string text;
        try
        {
            text = textDispatcher.Render(merged);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Single-entity reindex: render failed for {Id}", merged.Id);
            throw;
        }

        var finalEnvelope = WithRenderedText(merged, text, record);

        var vectors = await embeddings.EmbedAsync([text], ct);
        var point = new EntityPoint(finalEnvelope, vectors[0], record.FileHash);

        // Upsert only this one point — DO NOT call DeleteByFileHashExceptAsync.
        await store.UpsertAsync([point], ct);

        logger.LogInformation(
            "Single-entity reindex complete: book {BookId}, entity {EntityId}",
            bookId, entityId);
    }


    // ── Shared enrich/merge/render helpers (SIM-08/STR-08) ─────────────────────
    // Used by both IngestEntitiesAsync (batch, per-book) and ReindexEntityAsync
    // (single entity). The batch caller pre-fetches `existingQdrant` for all entity ids in
    // one round trip; the single-entity caller fetches it lazily only when there is no
    // 5etools match — both preserved exactly as before factoring.

    private async Task<IReadOnlyDictionary<string, EntityEnvelope>> BuildFivetoolsIndexAsync(
        IngestionRecord record, string warningMessage, CancellationToken ct)
    {
        IReadOnlyCollection<string>? sourceFilter = record.FivetoolsSourceKey is { } fk
            ? [fk] : null;

        try
        {
            return await FivetoolsRecordIndex.BuildAsync(_opts.FivetoolsDirectory, sourceFilter, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, warningMessage);
            return new Dictionary<string, EntityEnvelope>();
        }
    }

    private static EntityEnvelope MergeEnrichment(
        EntityEnvelope envelope,
        IReadOnlyDictionary<string, EntityEnvelope> fivetoolsIndex,
        IReadOnlyDictionary<string, EntityEnvelope> existingQdrant,
        out bool matchedFivetools)
    {
        if (fivetoolsIndex.TryGetValue(envelope.Id, out var fivetoolsEnvelope))
        {
            // 5etools file record exists → use it as the enrichment "existing".
            // This gives us clean structured values, SRD flags, keywords, clean name.
            matchedFivetools = true;
            return EntityMerger.Merge(envelope, fivetoolsEnvelope);
        }

        matchedFivetools = false;
        if (existingQdrant.TryGetValue(envelope.Id, out var qdrantEnvelope))
        {
            // No 5etools match but there is a prior Qdrant point — use it
            // (preserves manual corrections from prior ingests, etc.).
            return EntityMerger.Merge(envelope, qdrantEnvelope);
        }

        // No enrichment source at all — ingest canonical as-is.
        return envelope;
    }

    private static EntityEnvelope WithRenderedText(EntityEnvelope merged, string text, IngestionRecord record)
    {
        var ds = string.IsNullOrEmpty(merged.DataSource) ? "llm" : merged.DataSource;
        var sourceBook = record.FivetoolsSourceKey ?? merged.SourceBook;
        return merged with { CanonicalText = text, DataSource = ds, SourceBook = sourceBook };
    }
}