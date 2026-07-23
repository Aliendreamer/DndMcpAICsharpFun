using System.Text.Json;
using System.Text.Json.Nodes;

using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers;

/// <summary>
/// <see cref="IFivetoolsBackfillProvider"/> for <see cref="EntityType.Background"/>. Reads
/// <c>backgrounds.json</c>'s <c>"background"</c> array (no filter — every background qualifies)
/// and projects a curated <see cref="Domain.Entities.Fields.BackgroundFields"/> shape —
/// self-contained, like <see cref="GodBackfillProvider"/>/<see cref="SpellBackfillProvider"/>,
/// NOT the generic field-fill mapper's raw clone.
/// </summary>
public sealed class BackgroundBackfillProvider : IFivetoolsBackfillProvider
{
    public EntityType Type => EntityType.Background;

    /// <summary>Raw 5etools background records from backgrounds.json's "background" array (no filter).</summary>
    public IEnumerable<JsonElement> EnumerateRoster(string fivetoolsDir)
    {
        var path = Path.Combine(fivetoolsDir, "backgrounds.json");
        if (!File.Exists(path)) yield break;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(File.ReadAllBytes(path)); }
        catch (JsonException) { yield break; }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("background", out var arr)
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
            Id: EntityIdSlug.For(sourceKey, EntityType.Background, name),
            Type: EntityType.Background,
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
    /// Builds the canonical Background <c>fields</c> shape (see
    /// <see cref="Domain.Entities.Fields.BackgroundFields"/>): skill/tool/language proficiency
    /// lists flattened from the raw <c>skillProficiencies</c>/<c>toolProficiencies</c>/
    /// <c>languageProficiencies</c> grant-groups, a flattened equipment list from
    /// <c>startingEquipment</c>, and the named feature (from the <c>entries[]</c> block flagged
    /// <c>data.isFeature</c> or titled "Feature: ...").
    /// </summary>
    private static JsonElement BuildFields(JsonElement background)
    {
        var (featureName, featureSummary) = GetFeature(background);

        var fields = new JsonObject
        {
            ["skillProficiencies"] = ToJsonArray(GetProficiencyList(background, "skillProficiencies")),
            ["toolProficiencies"] = ToJsonArray(GetProficiencyList(background, "toolProficiencies")),
            ["languages"] = ToJsonArray(GetProficiencyList(background, "languageProficiencies")),
            ["equipment"] = ToJsonArray(GetEquipment(background)),
            ["featureName"] = featureName,
            ["featureSummary"] = featureSummary,
        };

        return JsonDocument.Parse(fields.ToJsonString()).RootElement.Clone();
    }

    private static IReadOnlyList<string> GetProficiencyList(JsonElement background, string prop)
    {
        if (!background.TryGetProperty(prop, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var result = new List<string>();
        foreach (var group in arr.EnumerateArray())
        {
            if (group.ValueKind != JsonValueKind.Object) continue;
            foreach (var p in group.EnumerateObject())
            {
                if (p.Name == "choose" && p.Value.ValueKind == JsonValueKind.Object)
                    result.Add(SummarizeChoose(p.Value));
                else if (p.Value.ValueKind == JsonValueKind.True)
                    result.Add(Titleize(p.Name));
                else if (p.Value.ValueKind == JsonValueKind.Number)
                    result.Add(p.Name == "anyStandard"
                        ? $"{p.Value.GetInt32()} of your choice"
                        : $"{Titleize(p.Name)} x{p.Value.GetInt32()}");
            }
        }
        return result;
    }

    private static string SummarizeChoose(JsonElement choose)
    {
        var count = choose.TryGetProperty("count", out var c) && c.TryGetInt32(out var ci) ? ci : 1;
        var from = choose.TryGetProperty("from", out var f) && f.ValueKind == JsonValueKind.Array
            ? string.Join(", ", f.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => Titleize(x.GetString()!)))
            : "";
        return string.IsNullOrEmpty(from) ? $"Choose {count}" : $"Choose {count} from {from}";
    }

    private static IReadOnlyList<string> GetEquipment(JsonElement background)
    {
        if (!background.TryGetProperty("startingEquipment", out var eq) || eq.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var result = new List<string>();
        CollectEquipmentItems(eq, result);
        return result;
    }

    private static void CollectEquipmentItems(JsonElement el, List<string> result)
    {
        switch (el.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in el.EnumerateArray())
                    CollectEquipmentItems(item, result);
                break;
            case JsonValueKind.Object:
                if (el.TryGetProperty("displayName", out var dn) && dn.ValueKind == JsonValueKind.String)
                    result.Add(StripSourceTag(dn.GetString()!));
                else if (el.TryGetProperty("item", out var it) && it.ValueKind == JsonValueKind.String)
                    result.Add(StripSourceTag(it.GetString()!));
                else if (el.TryGetProperty("special", out var sp) && sp.ValueKind == JsonValueKind.String)
                    result.Add(StripSourceTag(sp.GetString()!));
                else
                {
                    foreach (var prop in el.EnumerateObject())
                        CollectEquipmentItems(prop.Value, result);
                }
                break;
            case JsonValueKind.String:
                result.Add(StripSourceTag(el.GetString()!));
                break;
        }
    }

    private static string StripSourceTag(string s)
    {
        var idx = s.IndexOf('|');
        return idx >= 0 ? s[..idx] : s;
    }

    private static (string Name, string Summary) GetFeature(JsonElement background)
    {
        if (background.TryGetProperty("entries", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in arr.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;

                var isFeature = entry.TryGetProperty("data", out var data)
                    && data.ValueKind == JsonValueKind.Object
                    && data.TryGetProperty("isFeature", out var isf)
                    && isf.ValueKind == JsonValueKind.True;

                var name = entry.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                    ? n.GetString()!
                    : "";

                if (isFeature || name.StartsWith("Feature:", StringComparison.OrdinalIgnoreCase))
                {
                    var featureName = name.StartsWith("Feature:", StringComparison.OrdinalIgnoreCase)
                        ? name["Feature:".Length..].Trim()
                        : name;
                    var summary = entry.TryGetProperty("entries", out var fe) && fe.ValueKind == JsonValueKind.Array
                        ? FivetoolsEntryText.Flatten(fe)
                        : "";
                    return (featureName, summary);
                }
            }
        }
        return ("", "");
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