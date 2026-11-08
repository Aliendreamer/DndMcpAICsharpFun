using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Ingestion.Programmatic;

public sealed record ApplyOverridesResult(int Applied, int Accepted, int NeedsReview);

public interface IOverridesApplier
{
    /// <summary>Null when the book is unknown; throws <see cref="NoProgrammaticFileException"/> without a programmatic canonical file.</summary>
    Task<ApplyOverridesResult?> ApplyAsync(int bookId, CancellationToken ct);
}

public sealed class NoProgrammaticFileException(string message) : Exception(message);

/// <summary>
/// Re-applies a book's overrides and approvals to its CURRENT canonical file (no extraction), writes the file and
/// re-ingests the book into dnd_entities (entities that are not Accepted are removed there).
/// </summary>
public sealed class OverridesApplier(
    IIngestionTracker tracker,
    CanonicalJsonLoader loader,
    CanonicalJsonWriter writer,
    OverridesStore overridesStore,
    IEntityIngestionOrchestrator ingestion,
    CanonicalBookLocks locks,
    IOptions<EntityIngestionOptions> entityOptions) : IOverridesApplier
{
    public async Task<ApplyOverridesResult?> ApplyAsync(int bookId, CancellationToken ct)
    {
        var record = await tracker.GetByIdAsync(bookId, ct);
        if (record is null) return null;

        var slug = EntityIdSlug.BookSlug(record);
        var path = Path.Combine(entityOptions.Value.CanonicalDirectory, slug + ".json");
        using (await locks.AcquireAsync(slug, ct))
        {
            if (!File.Exists(path))
                throw new NoProgrammaticFileException($"No canonical file at {path}.");
            var file = await loader.LoadAsync(path, ct);
            if (file.SchemaVersion != CanonicalJsonSchema.CurrentVersion)
                throw new NoProgrammaticFileException($"{slug}.json is not a programmatic (schema \"{CanonicalJsonSchema.CurrentVersion}\") file.");

            var overrides = await overridesStore.LoadAsync(slug, ct);
            var merged = OverrideMerger.Reapply(file.Entities, overrides);
            await writer.WriteAsync(path, file with { Entities = merged.Entities }, ct);
            await ingestion.IngestEntitiesAsync(bookId, ct);

            var ids = merged.Entities.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
            var applied = overrides.Overrides.Count(x => ids.Contains(x.EntityId));
            return new ApplyOverridesResult(
                applied,
                merged.Entities.Count(e => e.Disposition == EntityDisposition.Accepted),
                merged.Entities.Count(e => e.Disposition == EntityDisposition.NeedsReview));
        }
    }
}
