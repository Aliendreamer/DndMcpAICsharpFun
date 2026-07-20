using System.Text.Json;
using System.Text.RegularExpressions;

using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

public sealed record ResolutionArtifacts(
    IReadOnlyList<CanonicalTable> Tables, IReadOnlyList<CanonicalChoiceSet> ChoiceSets);

/// <summary>
/// Emits the RESOLUTION-shaped Draconic Ancestry artifacts that drive CharacterResolutionService
/// breath-weapon resolution for PHB: a normalized table (ancestry/damageType/breathArea/saveAbility),
/// a companion choiceset, and the breath-damage-by-tier table. Distinct from the generic captioned
/// projection, which cedes the phb14.table.draconic-ancestry id to this.
/// </summary>
public static partial class DraconicAncestryResolutionProjector
{
    private const string TableId = "phb14.table.draconic-ancestry";
    private const string ChoiceSetId = "phb14.choiceset.draconic-ancestry";
    private const string TierTableId = "phb14.table.breath-damage-by-tier";

    [GeneratedRegex(@"^(?<area>.*?)\s*\((?<ab>[A-Za-z]+)\.?\s*save\)\s*$")]
    private static partial Regex BreathCell();

    private static readonly IReadOnlyDictionary<string, string> Ability =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Str"] = "Strength",
            ["Dex"] = "Dexterity",
            ["Con"] = "Constitution",
            ["Int"] = "Intelligence",
            ["Wis"] = "Wisdom",
            ["Cha"] = "Charisma",
        };

    public static ResolutionArtifacts Project(string fivetoolsDir, string sourceKey)
    {
        if (EntityIdSlug.BookSlug(sourceKey) != "phb14") return new([], []);

        var racesPath = Path.Combine(fivetoolsDir, "races.json");
        if (!File.Exists(racesPath)) return new([], []);
        using var doc = JsonDocument.Parse(File.ReadAllText(racesPath));
        if (!doc.RootElement.TryGetProperty("race", out var races) || races.ValueKind != JsonValueKind.Array)
            return new([], []);

        JsonElement? draconic = null;
        int? page = null;
        foreach (var race in races.EnumerateArray())
        {
            if (race.TryGetProperty("name", out var n) && n.GetString() == "Dragonborn"
                && race.TryGetProperty("source", out var s) && s.GetString() == sourceKey)
            {
                page = race.TryGetProperty("page", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;
                draconic = FindDraconicTable(race);
                break;
            }
        }
        if (draconic is null) return new([], []);

        var prov = new ProvenanceRef("phb14.5etools", sourceKey, page);
        CanonicalCell Cell(string v) => new(v, prov);

        var rows = new List<CanonicalTableRow>();
        var options = new List<CanonicalChoiceOption>();
        var i = 0;
        foreach (var r in draconic.Value.GetProperty("rows").EnumerateArray())
        {
            var ancestry = r[0].GetString() ?? "";
            var damageType = (r[1].GetString() ?? "").ToLowerInvariant();
            var (area, save) = ParseBreath(r[2].GetString() ?? "");
            rows.Add(new CanonicalTableRow([Cell(ancestry), Cell(damageType), Cell(area), Cell(save)]));
            options.Add(new CanonicalChoiceOption(ancestry, TableId, i, null));
            i++;
        }

        var draconicTable = new CanonicalTable(
            TableId, "Draconic Ancestry", ["ancestry", "damageType", "breathArea", "saveAbility"], rows);

        var tierTable = new CanonicalTable(
            TierTableId, "Breath Weapon Damage by Tier", ["tier", "dice"],
            [
                new([Cell("1"), Cell("1d10")]),
                new([Cell("2"), Cell("2d6")]),
                new([Cell("3"), Cell("3d6")]),
                new([Cell("4"), Cell("4d6")]),
            ]);

        var choiceSet = new CanonicalChoiceSet(ChoiceSetId, "Draconic Ancestry", options);

        return new([draconicTable, tierTable], [choiceSet]);
    }

    private static JsonElement? FindDraconicTable(JsonElement node)
    {
        switch (node.ValueKind)
        {
            case JsonValueKind.Object:
                if (node.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String && t.GetString() == "table"
                    && node.TryGetProperty("caption", out var c) && c.ValueKind == JsonValueKind.String
                    && c.GetString() == "Draconic Ancestry"
                    && node.TryGetProperty("rows", out _))
                    return node;
                foreach (var p in node.EnumerateObject())
                {
                    var found = FindDraconicTable(p.Value);
                    if (found is not null) return found;
                }
                break;
            case JsonValueKind.Array:
                foreach (var e in node.EnumerateArray())
                {
                    var found = FindDraconicTable(e);
                    if (found is not null) return found;
                }
                break;
        }
        return null;
    }

    private static (string Area, string Save) ParseBreath(string cell)
    {
        var m = BreathCell().Match(cell);
        if (!m.Success) return (cell.Trim(), "");
        var area = m.Groups["area"].Value.Trim();
        var save = Ability.TryGetValue(m.Groups["ab"].Value, out var full) ? full : m.Groups["ab"].Value;
        return (area, save);
    }
}