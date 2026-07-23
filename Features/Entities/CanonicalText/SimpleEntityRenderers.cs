using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DndMcpAICsharpFun.Features.Entities.CanonicalText;

public interface ISimpleEntityRenderer
{
    string Render(string name, JsonElement fields);
}

internal static partial class RendererHelpers
{
    [GeneratedRegex(@"\{@\w+\s([^|}]+)[^}]*\}")]
    private static partial Regex TagRx();
    private static readonly FrozenDictionary<string, string> SizeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    { ["T"] = "Tiny", ["S"] = "Small", ["M"] = "Medium", ["L"] = "Large", ["H"] = "Huge", ["G"] = "Gargantuan" }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    private static readonly FrozenDictionary<string, string> AlignMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    { ["L"] = "Lawful", ["C"] = "Chaotic", ["G"] = "Good", ["E"] = "Evil", ["N"] = "Neutral", ["U"] = "Unaligned", ["A"] = "Any" }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public static string StripTags(string s) => TagRx().Replace(s, "$1");
    public static string MapSize(string code) => SizeMap.TryGetValue(code, out var v) ? v : code;
    public static string MapAlign(string code) => AlignMap.TryGetValue(code, out var v) ? v : code;

    public static string FirstEntryText(JsonElement fields)
    {
        if (!fields.TryGetProperty("entries", out var entries)) return string.Empty;
        if (entries.ValueKind != JsonValueKind.Array) return string.Empty;
        var first = entries.EnumerateArray().FirstOrDefault();
        if (first.ValueKind == JsonValueKind.String)
            return StripTags(first.GetString()!);
        return string.Empty;
    }

    public static string StringProp(JsonElement e, string key)
    {
        return e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()! : string.Empty;
    }


    /// <summary>Copies a raw numeric property as an <c>int</c>, else <c>null</c> — safe against
    /// a JSON <c>null</c>/wrong-kind value (e.g. backfilled <c>"value": null</c>), which would
    /// otherwise make <see cref="JsonElement.TryGetInt32"/> throw <see cref="InvalidOperationException"/>
    /// (it only returns <c>false</c> for overflow/format issues on a Number-kind element — a
    /// non-Number ValueKind throws).</summary>
    public static int? IntProp(JsonElement e, string key)
    {
        return e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)
            ? n : null;
    }


    /// <summary>Safely reads a nested "sub-property" of a JsonElement (e.g. the "ac" inside an
    /// AC-source object, or the "cr" inside a lair-CR object) as a display string, tolerating
    /// EITHER a String or a Number ValueKind — 5etools-derived data is inconsistent about which
    /// shape a given sub-property takes, and both <see cref="JsonElement.GetString"/> and
    /// <see cref="JsonElement.TryGetInt32"/> THROW <see cref="InvalidOperationException"/> for a
    /// ValueKind they don't expect (TryGetXxx on JsonElement only "tries" for overflow/parse
    /// failures on an already-correct-kind element, not for the wrong ValueKind). Returns
    /// <c>null</c> if the property is missing or is neither a String nor a Number.</summary>
    public static string? NumberOrStringProp(JsonElement e, string key)
    {
        if (!e.TryGetProperty(key, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        };
    }

    public static IEnumerable<string> StringArray(JsonElement e, string key)
    {
        if (!e.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Enumerable.Empty<string>();
        return arr.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString()!);
    }


    /// <summary>
    /// Extracts a (feature name, level) pair from a class/subclass feature entry, which may be either
    /// a pipe-delimited reference string (e.g. "Rage|Barbarian|PHB||1") or an object with a named
    /// feature-reference property (e.g. "classFeature"/"subclassFeature") plus a numeric "level".
    /// <paramref name="minParts"/> is the minimum number of pipe-delimited segments required for the
    /// string form to be considered valid (class and subclass feature strings use different minimums).
    /// </summary>
    public static (string Name, int Level)? ExtractFeatureEntry(JsonElement element, string objectKey, int minParts = 2)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            var raw = element.GetString() ?? string.Empty;
            var parts = raw.Split('|');
            if (parts.Length >= minParts && int.TryParse(parts[^1], out var lv))
                return (parts[0], lv);
            return null;
        }
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(objectKey, out var cf) && cf.ValueKind == JsonValueKind.String
            && element.TryGetProperty("level", out var lv2) && lv2.ValueKind == JsonValueKind.Number
            && lv2.TryGetInt32(out var levelInt))
        {
            var raw = cf.GetString() ?? string.Empty;
            var name = raw.Split('|')[0];
            return (name, levelInt);
        }
        return null;
    }
}

public sealed class RaceCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var sb = new StringBuilder();
        var sizes = RendererHelpers.StringArray(f, "size")
            .Select(RendererHelpers.MapSize).ToList();
        var sizeText = sizes.Count > 0 ? string.Join("/", sizes) : "Unknown";
        sb.Append($"{name} — {sizeText} race.");
        if (f.TryGetProperty("speed", out var spd))
        {
            var spdText = spd.ValueKind == JsonValueKind.Number ? $" Speed: {spd.GetInt32()} ft." : string.Empty;
            sb.Append(spdText);
        }
        var traits = RendererHelpers.StringArray(f, "traitTags").ToList();
        if (traits.Count > 0) sb.Append($" Traits: {string.Join(", ", traits)}.");
        var summary = RendererHelpers.FirstEntryText(f);
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}

public sealed class SubraceCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var raceName = RendererHelpers.StringProp(f, "raceName");
        var sb = new StringBuilder($"{name}");
        if (!string.IsNullOrEmpty(raceName)) sb.Append($" ({raceName} subrace).");
        var summary = RendererHelpers.FirstEntryText(f);
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}

public sealed class BackgroundCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var sb = new StringBuilder($"{name}.");
        if (f.TryGetProperty("skillProficiencies", out var sp) && sp.ValueKind == JsonValueKind.Array)
        {
            var skills = sp.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.Object)
                .SelectMany(x => x.EnumerateObject().Select(p => p.Name))
                .ToList();
            if (skills.Count > 0) sb.Append($" Skills: {string.Join(", ", skills)}.");
        }
        var summary = RendererHelpers.FirstEntryText(f);
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}

public sealed class FeatCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var sb = new StringBuilder($"{name}.");
        if (f.TryGetProperty("prerequisite", out var pre) && pre.ValueKind == JsonValueKind.Array)
        {
            var preText = pre.GetRawText();
            if (preText.Length > 2) sb.Append($" Prerequisite: {preText}.");
        }
        var summary = RendererHelpers.FirstEntryText(f);
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}

public sealed class ItemCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var type = RendererHelpers.StringProp(f, "type");
        var sb = new StringBuilder($"{name}.");
        if (!string.IsNullOrEmpty(type)) sb.Append($" Type: {type}.");
        var value = RendererHelpers.IntProp(f, "value");
        if (value.HasValue) sb.Append($" Value: {value.Value / 100} gp.");
        var summary = RendererHelpers.FirstEntryText(f);
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}

public sealed class MagicItemCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var rarity = RendererHelpers.StringProp(f, "rarity");
        var type = RendererHelpers.StringProp(f, "type");
        var sb = new StringBuilder($"{name}. {rarity} magic item");
        if (!string.IsNullOrEmpty(type)) sb.Append($" ({type})");
        sb.Append('.');
        var reqAttune = f.TryGetProperty("reqAttune", out var ra)
            && ra.ValueKind != JsonValueKind.False && ra.ValueKind != JsonValueKind.Null;
        if (reqAttune) sb.Append(" Requires attunement.");
        var summary = RendererHelpers.FirstEntryText(f);
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}

public sealed class WeaponCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var cat = RendererHelpers.StringProp(f, "weaponCategory");
        var dmg = RendererHelpers.StringProp(f, "dmg1");
        var dmgType = RendererHelpers.StringProp(f, "dmgType");
        var sb = new StringBuilder($"{name}.");
        if (!string.IsNullOrEmpty(cat)) sb.Append($" {cat} weapon.");
        if (!string.IsNullOrEmpty(dmg)) sb.Append($" Damage: {dmg} {dmgType}.");
        var summary = RendererHelpers.FirstEntryText(f);
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}

public sealed class ArmorCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var type = RendererHelpers.StringProp(f, "type");
        var sb = new StringBuilder($"{name}. {type} armor.");
        var ac = RendererHelpers.IntProp(f, "ac");
        if (ac.HasValue) sb.Append($" AC: {ac.Value}.");
        var summary = RendererHelpers.FirstEntryText(f);
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}

public sealed class GodCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var pantheon = RendererHelpers.StringProp(f, "pantheon");
        var symbol = RendererHelpers.StringProp(f, "symbol");
        var aligns = RendererHelpers.StringArray(f, "alignment")
            .Select(RendererHelpers.MapAlign).ToList();
        var domains = RendererHelpers.StringArray(f, "domains").ToList();
        var sb = new StringBuilder($"{name}.");
        if (!string.IsNullOrEmpty(pantheon)) sb.Append($" {pantheon} pantheon.");
        if (aligns.Count > 0) sb.Append($" Alignment: {string.Join(" ", aligns)}.");
        if (domains.Count > 0) sb.Append($" Domains: {string.Join(", ", domains)}.");
        if (!string.IsNullOrEmpty(symbol)) sb.Append($" Symbol: {symbol}.");
        var summary = RendererHelpers.FirstEntryText(f);
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}

public sealed class TrapCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var trapType = RendererHelpers.StringProp(f, "trapHazType");
        var sb = new StringBuilder($"{name}.");
        if (!string.IsNullOrEmpty(trapType)) sb.Append($" {trapType} trap.");
        var summary = RendererHelpers.FirstEntryText(f);
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}

public sealed class ConditionCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var summary = RendererHelpers.FirstEntryText(f);
        return string.IsNullOrEmpty(summary) ? name : $"{name}. {summary}";
    }
}

public sealed class DiseasePoisonCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var summary = RendererHelpers.FirstEntryText(f);
        return string.IsNullOrEmpty(summary) ? name : $"{name}. {summary}";
    }
}

public sealed class VehicleMountCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var vType = RendererHelpers.StringProp(f, "vehicleType");
        var sb = new StringBuilder($"{name}.");
        if (!string.IsNullOrEmpty(vType)) sb.Append($" {vType}.");
        var summary = RendererHelpers.FirstEntryText(f);
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}

public sealed class PlaneCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var cat = RendererHelpers.StringProp(f, "category");
        var summary = RendererHelpers.FirstEntryText(f);
        var sb = new StringBuilder($"{name}.");
        if (!string.IsNullOrEmpty(cat)) sb.Append($" {cat}.");
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}

public sealed class FactionCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var hq = RendererHelpers.StringProp(f, "headquarters");
        var goals = RendererHelpers.StringArray(f, "goals").Take(2).ToList();
        var sb = new StringBuilder($"{name}.");
        if (!string.IsNullOrEmpty(hq)) sb.Append($" HQ: {hq}.");
        if (goals.Count > 0) sb.Append($" Goals: {string.Join("; ", goals)}.");
        var summary = RendererHelpers.FirstEntryText(f);
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}

public sealed class LocationCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var cat = RendererHelpers.StringProp(f, "category");
        var setting = RendererHelpers.StringProp(f, "setting");
        var sb = new StringBuilder($"{name}.");
        if (!string.IsNullOrEmpty(cat)) sb.Append($" {cat}");
        if (!string.IsNullOrEmpty(setting)) sb.Append($" in {setting}");
        sb.Append('.');
        var summary = RendererHelpers.FirstEntryText(f);
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}

public sealed class LoreCanonicalTextRenderer : ISimpleEntityRenderer
{
    public string Render(string name, JsonElement f)
    {
        var cat = RendererHelpers.StringProp(f, "category");
        var summary = RendererHelpers.FirstEntryText(f);
        var sb = new StringBuilder($"{name}.");
        if (!string.IsNullOrEmpty(cat)) sb.Append($" {cat} lore.");
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}

public sealed class RuleCanonicalTextRenderer : ISimpleEntityRenderer
{
    private static readonly FrozenDictionary<string, string> RuleTypeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    { ["C"] = "core", ["O"] = "optional", ["V"] = "variant" }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public string Render(string name, JsonElement f)
    {
        var ruleType = RendererHelpers.StringProp(f, "ruleType");
        var ruleDisplay = RuleTypeMap.TryGetValue(ruleType, out var d) ? d : ruleType;
        var summary = RendererHelpers.FirstEntryText(f);
        var sb = new StringBuilder($"{name}.");
        if (!string.IsNullOrEmpty(ruleDisplay)) sb.Append($" {ruleDisplay} rule.");
        if (!string.IsNullOrEmpty(summary)) sb.Append($" {summary}");
        return sb.ToString();
    }
}