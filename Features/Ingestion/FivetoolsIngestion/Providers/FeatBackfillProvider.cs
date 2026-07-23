using System.Text.Json;
using System.Text.Json.Nodes;

using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers;

/// <summary>
/// <see cref="IFivetoolsBackfillProvider"/> for <see cref="EntityType.Feat"/>. Reads
/// <c>feats.json</c>'s <c>"feat"</c> array (no filter — every feat qualifies) and projects a
/// curated <see cref="Domain.Entities.Fields.FeatFields"/> shape — self-contained, like
/// <see cref="GodBackfillProvider"/>/<see cref="SpellBackfillProvider"/>, NOT the generic
/// field-fill mapper's raw clone.
/// </summary>
public sealed class FeatBackfillProvider : IFivetoolsBackfillProvider
{
    public EntityType Type => EntityType.Feat;

    private static readonly (string Key, string Label)[] GrantKeys =
    {
        ("ability", "Ability Score Increase"),
        ("skillProficiencies", "Skill Proficiency"),
        ("toolProficiencies", "Tool Proficiency"),
        ("languageProficiencies", "Language"),
        ("expertise", "Expertise"),
        ("additionalSpells", "Spells"),
        ("resist", "Resistance"),
        ("senses", "Senses"),
        ("armorProficiencies", "Armor Proficiency"),
        ("weaponProficiencies", "Weapon Proficiency"),
    };

    /// <summary>Raw 5etools feat records from feats.json's "feat" array (no filter — every feat is a candidate).</summary>
    public IEnumerable<JsonElement> EnumerateRoster(string fivetoolsDir)
    {
        var path = Path.Combine(fivetoolsDir, "feats.json");
        if (!File.Exists(path)) yield break;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(File.ReadAllBytes(path)); }
        catch (JsonException) { yield break; }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("feat", out var arr)
                || arr.ValueKind != JsonValueKind.Array)
                yield break;

            foreach (var el in arr.EnumerateArray())
                yield return el.Clone();
        }
    }

    public EntityEnvelope BuildEntity(string sourceKey, string edition, string name, JsonElement element)
    {
        int? page = element.TryGetProperty("page", out var pg) && pg.TryGetInt32(out var pv) ? pv : null;
        var srd = element.TryGetProperty("srd", out var s) && s.ValueKind == JsonValueKind.True;
        var srd52 = element.TryGetProperty("srd52", out var s2) && s2.ValueKind == JsonValueKind.True;

        return new EntityEnvelope(
            Id: EntityIdSlug.For(sourceKey, EntityType.Feat, name),
            Type: EntityType.Feat,
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
    /// Builds the canonical Feat <c>fields</c> shape (see
    /// <see cref="Domain.Entities.Fields.FeatFields"/>): a human-readable prerequisite summary,
    /// a description assembled from <c>entries[]</c>, and a coarse list of grant categories
    /// (ability score increase, proficiencies, spells, etc.) present on the raw element.
    /// </summary>
    private static JsonElement BuildFields(JsonElement feat)
    {
        var fields = new JsonObject
        {
            ["prerequisites"] = ToJsonArray(GetPrerequisites(feat)),
            ["description"] = GetDescription(feat),
            ["grants"] = ToJsonArray(GetGrants(feat)),
        };

        return JsonDocument.Parse(fields.ToJsonString()).RootElement.Clone();
    }

    private static IReadOnlyList<string> GetPrerequisites(JsonElement feat)
    {
        if (!feat.TryGetProperty("prerequisite", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var result = new List<string>();
        foreach (var pre in arr.EnumerateArray())
        {
            if (pre.ValueKind != JsonValueKind.Object) continue;
            foreach (var prop in pre.EnumerateObject())
            {
                var summary = SummarizePrereq(prop.Name, prop.Value);
                if (!string.IsNullOrWhiteSpace(summary))
                    result.Add(summary);
            }
        }
        return result;
    }

    private static string SummarizePrereq(string key, JsonElement value)
    {
        switch (key)
        {
            case "level":
                return value.ValueKind == JsonValueKind.Number
                    ? $"Level {value.GetInt32()}"
                    : value.TryGetProperty("level", out var lv) && lv.ValueKind == JsonValueKind.Number
                        ? $"Level {lv.GetInt32()}"
                        : "Level";
            case "spellcasting" or "spellcasting2020" when value.ValueKind == JsonValueKind.True:
                return "Spellcasting";
            case "ability" when value.ValueKind == JsonValueKind.Array:
                return string.Join(" or ", value.EnumerateArray()
                    .Where(o => o.ValueKind == JsonValueKind.Object)
                    .SelectMany(o => o.EnumerateObject())
                    .Select(p => $"{Titleize(p.Name)} {(p.Value.ValueKind == JsonValueKind.Number ? p.Value.GetInt32().ToString() : "")}".Trim()));
            case "race" when value.ValueKind == JsonValueKind.Array:
                return string.Join(" or ", value.EnumerateArray()
                    .Where(o => o.ValueKind == JsonValueKind.Object)
                    .Select(o => o.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null)
                    .Where(n => !string.IsNullOrWhiteSpace(n)));
            default:
                return value.ValueKind is JsonValueKind.Array or JsonValueKind.Object
                    ? $"{Titleize(key)}: {value.GetRawText()}"
                    : $"{Titleize(key)}";
        }
    }

    private static IReadOnlyList<string> GetGrants(JsonElement feat)
    {
        var grants = new List<string>();
        foreach (var (key, label) in GrantKeys)
        {
            if (feat.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() > 0)
                grants.Add(label);
        }
        return grants;
    }

    private static string GetDescription(JsonElement feat)
    {
        if (!feat.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return "";
        return FivetoolsEntryText.Flatten(entries);
    }

    private static string Titleize(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static JsonArray ToJsonArray(IEnumerable<string> items)
    {
        var arr = new JsonArray();
        foreach (var i in items)
            arr.Add(i);
        return arr;
    }
}