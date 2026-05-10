using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Mappers;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using Microsoft.Extensions.Logging;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

public sealed class FivetoolsIngestionService(
    IEntityVectorStore store,
    IEmbeddingService embeddings,
    EntityCanonicalTextDispatcher dispatcher,
    ILogger<FivetoolsIngestionService> logger)
{
    private static readonly Dictionary<EntityType, IFivetoolsEntityMapper> Mappers = new()
    {
        [EntityType.Class]         = new FivetoolsClassMapper(),
        [EntityType.Subclass]      = new FivetoolsSubclassMapper(),
        [EntityType.Spell]         = new FivetoolsSpellMapper(),
        [EntityType.Monster]       = new FivetoolsMonsterMapper(),
        [EntityType.Race]          = new FivetoolsRaceMapper(),
        [EntityType.Subrace]       = new FivetoolsSubraceMapper(),
        [EntityType.Background]    = new FivetoolsBackgroundMapper(),
        [EntityType.Feat]          = new FivetoolsFeatMapper(),
        [EntityType.Item]          = new FivetoolsItemMapper(),
        [EntityType.MagicItem]     = new FivetoolsMagicItemMapper(),
        [EntityType.Weapon]        = new FivetoolsWeaponMapper(),
        [EntityType.Armor]         = new FivetoolsArmorMapper(),
        [EntityType.God]           = new FivetoolsGodMapper(),
        [EntityType.Trap]          = new FivetoolsTrapMapper(),
        [EntityType.Condition]     = new FivetoolsConditionMapper(),
        [EntityType.DiseasePoison] = new FivetoolsDiseasePoisonMapper(),
        [EntityType.VehicleMount]  = new FivetoolsVehicleMapper(),
        [EntityType.Rule]          = new FivetoolsRuleMapper(),
    };

    public async Task ImportAllAsync(CancellationToken ct = default)
    {
        var allEnvelopes = new List<EntityEnvelope>();

        foreach (var entry in FivetoolsSourceRegistry.AllEntries)
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(entry.RelativePath))
            {
                logger.LogWarning("5etools file not found, skipping: {Path}", entry.RelativePath);
                continue;
            }
            if (!Mappers.TryGetValue(entry.EntityType, out var mapper))
            {
                logger.LogWarning("No mapper for entity type {Type}, skipping {Path}", entry.EntityType, entry.RelativePath);
                continue;
            }

            await using var stream = File.OpenRead(entry.RelativePath);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (!doc.RootElement.TryGetProperty(entry.JsonArrayKey, out var arr)
                || arr.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var item in arr.EnumerateArray())
            {
                var envelope = mapper.Map(item);
                if (envelope is not null)
                    allEnvelopes.Add(envelope);
            }
        }

        logger.LogInformation("5etools import: {Count} entities mapped", allEnvelopes.Count);
        await IngestEnvelopesAsync(allEnvelopes, ct);
    }

    public async Task IngestEnvelopesAsync(IReadOnlyList<EntityEnvelope> envelopes, CancellationToken ct = default)
    {
        if (envelopes.Count == 0) return;

        var ids = envelopes.Select(e => e.Id).ToList();
        var existingSources = await store.GetDataSourcesAsync(ids, ct);
        var toIngest = envelopes
            .Where(e => !existingSources.TryGetValue(e.Id, out var src) || src != "manual")
            .ToList();

        if (toIngest.Count < envelopes.Count)
            logger.LogInformation("Skipped {Count} manually-corrected entities", envelopes.Count - toIngest.Count);

        var renderedEnvelopes = toIngest.Select(e =>
        {
            try { return e with { CanonicalText = dispatcher.Render(e) }; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to render canonical text for {Id}", e.Id);
                return e;
            }
        }).ToList();

        var texts = renderedEnvelopes.Select(e => e.CanonicalText).ToList();
        IList<float[]> vectors = texts.Count == 0
            ? Array.Empty<float[]>()
            : await embeddings.EmbedAsync(texts, ct);

        if (vectors.Count != renderedEnvelopes.Count)
            throw new InvalidOperationException(
                $"EmbedAsync returned {vectors.Count} vectors for {renderedEnvelopes.Count} texts.");

        var points = renderedEnvelopes
            .Select((e, i) => new EntityPoint(e, vectors[i], $"5etools:{e.SourceBook}"))
            .ToList();

        await store.UpsertAsync(points, ct);
        logger.LogInformation("5etools import: upserted {Count} entities", points.Count);
    }


    /// <summary>
    /// Builds a lookup of (name, sourceBook) → EntityType from all available 5etools source files.
    /// Used by the canonical type-fixer to correct LLM-assigned entity types.
    /// </summary>
    public async Task<IReadOnlyDictionary<(string name, string source), EntityType>> BuildTypeLookupAsync(
        CancellationToken ct = default)
    {
        // Use last-writer-wins: if the same (name, source) appears in multiple entries
        // (e.g. a subrace entry overrides a race entry for the same name), we keep the
        // more-specific type that was registered last in the registry.
        var lookup = new Dictionary<(string, string), EntityType>();

        foreach (var entry in FivetoolsSourceRegistry.AllEntries)
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(entry.RelativePath)) continue;
            if (!Mappers.TryGetValue(entry.EntityType, out var mapper)) continue;

            await using var stream = File.OpenRead(entry.RelativePath);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (!doc.RootElement.TryGetProperty(entry.JsonArrayKey, out var arr)
                || arr.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var item in arr.EnumerateArray())
            {
                if (!item.TryGetProperty("name", out var nameProp)
                    || nameProp.ValueKind != JsonValueKind.String)
                    continue;
                var name = nameProp.GetString();
                if (string.IsNullOrWhiteSpace(name)) continue;

                var source = item.TryGetProperty("source", out var src)
                    && src.ValueKind == JsonValueKind.String
                    ? src.GetString()! : "Unknown";

                lookup[(name.ToLowerInvariant(), source.ToUpperInvariant())] = entry.EntityType;
            }
        }

        return lookup;
    }
}
