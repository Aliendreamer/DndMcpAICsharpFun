using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

/// <summary>
/// Builds a read-only in-memory index of id → <see cref="EntityEnvelope"/> from the local
/// 5etools source files.  This is used at entity-ingest time to enrich canonical LLM-extracted
/// entities with clean 5etools structured data (SRD flags, scalars, keywords, clean names) via
/// <see cref="EntityMerger"/> — it does NOT touch Qdrant.
///
/// If the 5etools directory is absent the index is empty and callers should proceed unenriched.
/// </summary>
public static class FivetoolsRecordIndex
{
    private static readonly Dictionary<EntityType, IFivetoolsEntityMapper> Mappers = new()
    {
        [EntityType.Class]         = new Mappers.FivetoolsClassMapper(),
        [EntityType.Subclass]      = new Mappers.FivetoolsSubclassMapper(),
        [EntityType.Spell]         = new Mappers.FivetoolsSpellMapper(),
        [EntityType.Monster]       = new Mappers.FivetoolsMonsterMapper(),
        [EntityType.Race]          = new Mappers.FivetoolsRaceMapper(),
        [EntityType.Subrace]       = new Mappers.FivetoolsSubraceMapper(),
        [EntityType.Background]    = new Mappers.FivetoolsBackgroundMapper(),
        [EntityType.Feat]          = new Mappers.FivetoolsFeatMapper(),
        [EntityType.Item]          = new Mappers.FivetoolsItemMapper(),
        [EntityType.MagicItem]     = new Mappers.FivetoolsMagicItemMapper(),
        [EntityType.Weapon]        = new Mappers.FivetoolsWeaponMapper(),
        [EntityType.Armor]         = new Mappers.FivetoolsArmorMapper(),
        [EntityType.God]           = new Mappers.FivetoolsGodMapper(),
        [EntityType.Trap]          = new Mappers.FivetoolsTrapMapper(),
        [EntityType.Condition]     = new Mappers.FivetoolsConditionMapper(),
        [EntityType.DiseasePoison] = new Mappers.FivetoolsDiseasePoisonMapper(),
        [EntityType.VehicleMount]  = new Mappers.FivetoolsVehicleMapper(),
        [EntityType.Rule]          = new Mappers.FivetoolsRuleMapper(),
    };

    /// <summary>
    /// Builds the index from the given <paramref name="fivetoolsBaseDir"/> (defaults to "5etools").
    /// </summary>
    /// <param name="fivetoolsBaseDir">Root directory of the local 5etools data checkout.</param>
    /// <param name="sourceKeyFilter">
    /// Optional set of 5etools source keys (e.g. "PHB", "TCE") to restrict which records are
    /// loaded.  An entity record is included only when its source key matches one of the filter
    /// values (case-insensitive).  Pass <see langword="null"/> to include all sources.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<IReadOnlyDictionary<string, EntityEnvelope>> BuildAsync(
        string? fivetoolsBaseDir = null,
        IReadOnlyCollection<string>? sourceKeyFilter = null,
        CancellationToken ct = default)
    {
        var baseDir = fivetoolsBaseDir ?? "5etools";

        if (!Directory.Exists(baseDir))
        {
            Serilog.Log.Warning(
                "5etools data directory '{BaseDir}' not found — Fivetools enrichment index is empty. " +
                "Mount the 5etools data (COR-23) to enable import/backfill/enrichment.", baseDir);
            return new Dictionary<string, EntityEnvelope>();
        }

        // Build a normalised set of accepted source keys for fast lookup.
        HashSet<string>? filterSet = sourceKeyFilter is { Count: > 0 }
            ? new HashSet<string>(sourceKeyFilter, StringComparer.OrdinalIgnoreCase)
            : null;

        // Gather all registry entries that live under the requested base dir.
        // FivetoolsSourceRegistry uses the compile-time constant "5etools" as the base,
        // so we remap file paths relative to the standard base to the supplied base dir.
        var standardBase = "5etools";
        var index = new Dictionary<string, EntityEnvelope>(StringComparer.Ordinal);

        foreach (var entry in FivetoolsSourceRegistry.AllEntries)
        {
            ct.ThrowIfCancellationRequested();

            // Remap the path: entry.RelativePath starts with "5etools/" or "5etools\"
            var entryRelative = entry.RelativePath;
            string filePath;
            if (entryRelative.StartsWith(standardBase + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase)
                || entryRelative.StartsWith(standardBase + "/", StringComparison.OrdinalIgnoreCase))
            {
                var tail = entryRelative[(standardBase.Length + 1)..];
                filePath = Path.Combine(baseDir, tail);
            }
            else
            {
                filePath = entryRelative;
            }

            if (!File.Exists(filePath))
                continue;

            if (!Mappers.TryGetValue(entry.EntityType, out var mapper))
                continue;

            await using var stream = File.OpenRead(filePath);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (!doc.RootElement.TryGetProperty(entry.JsonArrayKey, out var arr)
                || arr.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var item in arr.EnumerateArray())
            {
                // Apply source-key filter before mapping (cheaper than mapping first).
                if (filterSet is not null)
                {
                    if (!item.TryGetProperty("source", out var srcProp)
                        || srcProp.ValueKind != JsonValueKind.String)
                        continue;
                    if (!filterSet.Contains(srcProp.GetString()!))
                        continue;
                }

                var envelope = mapper.Map(item);
                if (envelope is not null)
                    // Last-writer-wins on duplicate ids (mirrors FivetoolsIngestionService).
                    index[envelope.Id] = envelope;
            }
        }

        return index;
    }
}
