using System.Text.Json;
using System.Text.Json.Nodes;

using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers;

/// <summary>
/// <see cref="IFivetoolsBackfillProvider"/> for <see cref="EntityType.Spell"/>. Lifts the curated
/// field-projection logic verbatim from the retired <c>SpellBackfillService</c> (all-source
/// spells-*.json enumeration — the per-source filter is removed since the generic engine filters by
/// source — and the exact <c>BuildEntity</c>/<c>BuildFields</c> bodies) so the generic
/// <see cref="EntityBackfillService"/> can drive Spell backfill.
/// </summary>
public sealed class SpellBackfillProvider : IFivetoolsBackfillProvider
{
    public EntityType Type => EntityType.Spell;

    /// <summary>Raw 5etools spell records across every spells-*.json, every source (the engine filters by source key).</summary>
    public IEnumerable<JsonElement> EnumerateRoster(string fivetoolsDir)
    {
        var spellDir = Path.Combine(fivetoolsDir, "spells");
        if (!Directory.Exists(spellDir)) yield break;

        foreach (var path in Directory.GetFiles(spellDir, "spells-*.json")
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
                if (!doc.RootElement.TryGetProperty("spell", out var arr)
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
            Id: EntityIdSlug.For(sourceKey, EntityType.Spell, name),
            Type: EntityType.Spell,
            Name: name,
            SourceBook: sourceKey,
            Edition: edition,
            Page: page,
            FirstAppearedIn: new FirstAppearance(sourceKey, edition, page),
            RevisedIn: Array.Empty<Revision>(),
            SettingTags: Array.Empty<string>(),
            CanonicalText: "",
            Fields: BuildFields(element),
            DataSource: "5etools-backfill",
            Srd: srd,
            Srd52: srd52,
            BasicRules2024: false,
            NeedsReview: false,
            Keywords: Array.Empty<string>(),
            Disposition: EntityDisposition.Accepted);
    }

    /// <summary>
    /// Builds the canonical Spell <c>fields</c> shape: a single "Description" block wrapping the
    /// 5etools <c>entries</c>, plus <c>entriesHigherLevel</c> (when present) and
    /// <c>damageInflict</c>/<c>conditionInflict</c> (copied, else null) — mirroring parsed spells.
    /// </summary>
    private static JsonElement BuildFields(JsonElement spell)
    {
        var description = new JsonObject
        {
            ["type"] = "entries",
            ["name"] = "Description",
            ["entries"] = CopyOrEmptyArray(spell, "entries"),
        };

        var fields = new JsonObject { ["entries"] = new JsonArray(description) };

        if (spell.TryGetProperty("entriesHigherLevel", out var ehl) && ehl.ValueKind == JsonValueKind.Array)
            fields["entriesHigherLevel"] = JsonNode.Parse(ehl.GetRawText());

        fields["damageInflict"] = CopyOrNull(spell, "damageInflict");
        fields["conditionInflict"] = CopyOrNull(spell, "conditionInflict");

        return JsonDocument.Parse(fields.ToJsonString()).RootElement.Clone();
    }

    private static JsonNode CopyOrEmptyArray(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Array
            ? JsonNode.Parse(v.GetRawText())!
            : new JsonArray();

    private static JsonNode? CopyOrNull(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) && v.ValueKind != JsonValueKind.Null
            ? JsonNode.Parse(v.GetRawText())
            : null;
}