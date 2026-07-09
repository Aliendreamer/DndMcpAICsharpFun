using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

/// <summary>
/// Per-entity-type seam for the generic <see cref="EntityBackfillService"/>: supplies the roster of
/// qualifying 5etools elements for a type and builds a backfill <see cref="EntityEnvelope"/> for a
/// roster gap. The engine reads <c>name</c>/<c>source</c> off each <see cref="JsonElement"/> itself;
/// implementations only need to enumerate and build.
/// </summary>
public interface IFivetoolsBackfillProvider
{
    /// <summary>The canonical entity type this provider backfills (e.g. <see cref="EntityType.Monster"/>).</summary>
    EntityType Type { get; }

    /// <summary>Every QUALIFYING 5etools element of this type across all sources (all bestiary/spell files,
    /// or the single global file). MagicItem applies the rarity filter here. Name/source are read by the engine.</summary>
    IEnumerable<JsonElement> EnumerateRoster(string fivetoolsDir);

    /// <summary>Builds a backfill EntityEnvelope for a roster gap: curated *Fields projection,
    /// dataSource "5etools-backfill", Disposition Accepted, id/edition from the book key.</summary>
    EntityEnvelope BuildEntity(string sourceKey, string edition, string name, JsonElement element);
}