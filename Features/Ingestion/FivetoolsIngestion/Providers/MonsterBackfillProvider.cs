using System.Text.Json;
using System.Text.Json.Nodes;

using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers;

/// <summary>
/// <see cref="IFivetoolsBackfillProvider"/> for <see cref="EntityType.Monster"/>. Lifts the curated
/// field-projection logic verbatim from the retired <c>MonsterBackfillService</c> (all-source
/// bestiary-*.json enumeration and the exact <c>BuildEntity</c>/<c>BuildFields</c>/<c>GetKeywords</c>
/// bodies) so the generic <see cref="EntityBackfillService"/> can drive Monster backfill.
/// </summary>
public sealed class MonsterBackfillProvider : IFivetoolsBackfillProvider
{
    private const string BackfillDataSource = "5etools-backfill";

    public EntityType Type => EntityType.Monster;

    /// <summary>
    /// Raw 5etools monster records across every bestiary-*.json, every source (the engine filters by
    /// source key).
    /// </summary>
    public IEnumerable<JsonElement> EnumerateRoster(string fivetoolsDir)
    {
        var bestiaryDir = Path.Combine(fivetoolsDir, "bestiary");
        if (!Directory.Exists(bestiaryDir)) yield break;

        foreach (var path in Directory.GetFiles(bestiaryDir, "bestiary-*.json")
                     .Where(f =>
                     {
                         var n = Path.GetFileName(f);
                         return !n.StartsWith("fluff-", StringComparison.Ordinal)
                                && !n.Contains("index", StringComparison.Ordinal)
                                && !n.Contains("foundry", StringComparison.Ordinal);
                     })
                     .Order(StringComparer.Ordinal))
        {
            JsonDocument doc;
            try { doc = JsonDocument.Parse(File.ReadAllBytes(path)); }
            catch (JsonException) { continue; }

            using (doc)
            {
                if (!doc.RootElement.TryGetProperty("monster", out var arr)
                    || arr.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var el in arr.EnumerateArray())
                    yield return el.Clone();
            }
        }
    }

    public EntityEnvelope BuildEntity(string sourceKey, string edition, string name, JsonElement element)
    {
        int? page = element.TryGetProperty("page", out var pg) && pg.TryGetInt32(out var pv) ? pv : null;
        var srd = element.TryGetProperty("srd", out var s) && s.ValueKind == JsonValueKind.True;
        var srd52 = element.TryGetProperty("srd52", out var s2) && s2.ValueKind == JsonValueKind.True;

        return new EntityEnvelope(
            Id: EntityIdSlug.For(sourceKey, EntityType.Monster, name),
            Type: EntityType.Monster,
            Name: name,
            SourceBook: sourceKey,
            Edition: edition,
            Page: page,
            FirstAppearedIn: new FirstAppearance(sourceKey, edition, page),
            RevisedIn: Array.Empty<Revision>(),
            SettingTags: Array.Empty<string>(),
            CanonicalText: "",
            Fields: BuildFields(element),
            DataSource: BackfillDataSource,
            Srd: srd,
            Srd52: srd52,
            BasicRules2024: false,
            NeedsReview: false,
            Keywords: GetKeywords(element),
            Disposition: EntityDisposition.Accepted);
    }

    /// <summary>Copies the traitTags strings as keywords (mirrors <c>FivetoolsMonsterMapper.GetKeywords</c>).</summary>
    private static IReadOnlyList<string> GetKeywords(JsonElement monster)
    {
        if (!monster.TryGetProperty("traitTags", out var tags) || tags.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();
        return tags.EnumerateArray()
            .Where(t => t.ValueKind == JsonValueKind.String)
            .Select(t => t.GetString()!)
            .ToList();
    }

    /// <summary>
    /// The canonical Monster <c>fields</c> property names (see <c>Domain/Entities/Fields/MonsterFields.cs</c>);
    /// 5etools uses the identical property names, so the projection is a 1:1 copy of the present ones.
    /// </summary>
    private static readonly string[] MonsterFieldNames =
    {
        "size", "type", "alignment", "ac", "hp", "speed",
        "str", "dex", "con", "int", "wis", "cha",
        "save", "skill", "resist", "immune", "vulnerable", "conditionImmune",
        "senses", "passive", "languages", "cr",
        "trait", "action", "bonus", "reaction", "legendary", "legendaryHeader",
        "lair", "lairHeader", "spellcasting", "environment",
    };

    /// <summary>
    /// Builds the canonical Monster <c>fields</c> shape by copying each present monster stat-block
    /// property from the 5etools element.  Deserialises 1:1 as a <see cref="Domain.Entities.Fields.MonsterFields"/>.
    /// </summary>
    private static JsonElement BuildFields(JsonElement monster)
    {
        var fields = new JsonObject();
        foreach (var prop in MonsterFieldNames)
        {
            var node = CopyOrNull(monster, prop);
            if (node is not null)
                fields[prop] = node;
        }

        return JsonDocument.Parse(fields.ToJsonString()).RootElement.Clone();
    }

    private static JsonNode? CopyOrNull(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Null
            ? JsonNode.Parse(v.GetRawText())
            : null;
}