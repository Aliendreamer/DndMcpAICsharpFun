using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Ingestion.Entities;

public sealed class EntityIngestionOptions
{
    public string CanonicalDirectory { get; set; } = "data/canonical";
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

    public async Task IngestEntitiesAsync(int bookId, CancellationToken ct = default)
    {
        var record = await tracker.GetByIdAsync(bookId, ct)
                     ?? throw new InvalidOperationException($"No ingestion record {bookId}");

        var bookSlug = record.FivetoolsSourceKey is { } key
            ? EntityIdSlug.For(key, EntityType.Class, "x").Split('.')[0]
            : EntityIdSlug.For(record.DisplayName, EntityType.Class, "x").Split('.')[0];
        var path = Path.Combine(_opts.CanonicalDirectory, bookSlug + ".json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Canonical JSON not found for book {bookId} at {path}", path);

        var file = await loader.LoadAsync(path, ct);

        foreach (var w in refResolver.Resolve(file.Entities))
            logger.LogWarning("Dangling entity reference: {Source} -> {Target} ({Path})",
                w.SourceEntityId, w.MissingTargetId, w.FieldPath);

        // Pre-fetch existing Qdrant data for all entities to enable per-field merge.
        // (5etools points use a different file hash and are NOT deleted below.)
        var entityIds = file.Entities.Select(e => e.Id).ToList();
        var existing = await store.GetByIdsAsync(entityIds, ct);

        var renderedEnvelopes = new List<EntityEnvelope>(file.Entities.Count);
        var texts = new List<string>(file.Entities.Count);
        foreach (var envelope in file.Entities)
        {
            ct.ThrowIfCancellationRequested();

            // Merge with existing 5etools data if present.
            var merged = existing.TryGetValue(envelope.Id, out var existingEnvelope)
                ? EntityMerger.Merge(envelope, existingEnvelope)
                : envelope;

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
            var ds = string.IsNullOrEmpty(merged.DataSource) ? "llm" : merged.DataSource;
            var sourceBook = record.FivetoolsSourceKey ?? merged.SourceBook;
            renderedEnvelopes.Add(merged with { CanonicalText = text, DataSource = ds, SourceBook = sourceBook });
            texts.Add(text);
        }

        IList<float[]> vectors = texts.Count == 0
            ? Array.Empty<float[]>()
            : await embeddings.EmbedAsync(texts, ct);

        var points = new List<EntityPoint>(renderedEnvelopes.Count);
        for (int i = 0; i < renderedEnvelopes.Count; i++)
            points.Add(new EntityPoint(renderedEnvelopes[i], vectors[i], record.FileHash));

        // Upsert first — if this fails, the old vectors are still intact.
        await store.UpsertAsync(points, ct);

        // Delete old vectors only after upsert succeeds.
        // Guard: skip if FileHash is empty (book registered but never block-ingested).
        if (!string.IsNullOrEmpty(record.FileHash))
            await store.DeleteByFileHashAsync(record.FileHash, ct);

        await tracker.MarkEntitiesIngestedAsync(bookId, points.Count, ct);
        logger.LogInformation("Entity ingestion complete: book {BookId}, {Count} entities", bookId, points.Count);
    }
}
