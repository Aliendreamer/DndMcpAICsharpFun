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

        var bookSlug = EntityIdSlug
            .For(record.DisplayName, EntityType.Class, "x")
            .Split('.')[0];
        var path = Path.Combine(_opts.CanonicalDirectory, bookSlug + ".json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"Canonical JSON not found for book {bookId} at {path}", path);

        var file = await loader.LoadAsync(path, ct);

        foreach (var w in refResolver.Resolve(file.Entities))
            logger.LogWarning("Dangling entity reference: {Source} -> {Target} ({Path})",
                w.SourceEntityId, w.MissingTargetId, w.FieldPath);

        await store.DeleteByFileHashAsync(record.FileHash, ct);

        var renderedEnvelopes = new List<EntityEnvelope>(file.Entities.Count);
        var texts = new List<string>(file.Entities.Count);
        foreach (var envelope in file.Entities)
        {
            ct.ThrowIfCancellationRequested();
            var text = textDispatcher.Render(envelope);
            renderedEnvelopes.Add(envelope with { CanonicalText = text });
            texts.Add(text);
        }

        IList<float[]> vectors = texts.Count == 0
            ? Array.Empty<float[]>()
            : await embeddings.EmbedAsync(texts, ct);

        var points = new List<EntityPoint>(renderedEnvelopes.Count);
        for (int i = 0; i < renderedEnvelopes.Count; i++)
            points.Add(new EntityPoint(renderedEnvelopes[i], vectors[i], record.FileHash));

        await store.UpsertAsync(points, ct);

        await tracker.MarkEntitiesIngestedAsync(bookId, points.Count, ct);
        logger.LogInformation("Entity ingestion complete: book {BookId}, {Count} entities", bookId, points.Count);
    }
}
