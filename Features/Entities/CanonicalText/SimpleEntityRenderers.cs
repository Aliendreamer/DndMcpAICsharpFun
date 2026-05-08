using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DndMcpAICsharpFun.Features.Entities.CanonicalText;

public interface ISimpleEntityRenderer
{
    string Render(string name, JsonElement fields);
}

internal static class RendererHelpers
{
    private static readonly Regex TagRx = new(@"\{@\w+\s([^|}]+)[^}]*\}", RegexOptions.Compiled);
    private static readonly Dictionary<string, string> SizeMap = new(StringComparer.OrdinalIgnoreCase)
        { ["T"] = "Tiny", ["S"] = "Small", ["M"] = "Medium", ["L"] = "Large", ["H"] = "Huge", ["G"] = "Gargantuan" };
    private static readonly Dictionary<string, string> AlignMap = new(StringComparer.OrdinalIgnoreCase)
        { ["L"] = "Lawful", ["C"] = "Chaotic", ["G"] = "Good", ["E"] = "Evil", ["N"] = "Neutral", ["U"] = "Unaligned", ["A"] = "Any" };

    public static string StripTags(string s) => TagRx.Replace(s, "$1");
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

    public static IEnumerable<string> StringArray(JsonElement e, string key)
    {
        if (!e.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Enumerable.Empty<string>();
        return arr.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString()!);
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
        if (f.TryGetProperty("value", out var val) && val.TryGetInt32(out var v))
            sb.Append($" Value: {v / 100} gp.");
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
        if (f.TryGetProperty("ac", out var ac) && ac.TryGetInt32(out var acv))
            sb.Append($" AC: {acv}.");
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
    private static readonly Dictionary<string, string> RuleTypeMap = new(StringComparer.OrdinalIgnoreCase)
        { ["C"] = "core", ["O"] = "optional", ["V"] = "variant" };

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
