using System.Collections.Frozen;
using System.Text;
using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities.Fields;

namespace DndMcpAICsharpFun.Features.Entities.CanonicalText;

public sealed class MonsterCanonicalTextRenderer
{
    // NOTE: AlignMap intentionally stays local -- it diverges from RendererHelpers.AlignMap
    // ("A" => "Any alignment" here vs "Any" there, plus NX/NY keys not present in the shared
    // map). See SIM-05 consolidation report: this divergence was flagged rather than merged.
    private static readonly FrozenDictionary<string, string> AlignMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["L"] = "Lawful",
        ["C"] = "Chaotic",
        ["G"] = "Good",
        ["E"] = "Evil",
        ["N"] = "Neutral",
        ["U"] = "Unaligned",
        ["A"] = "Any alignment",
        ["NX"] = "non-chaotic",
        ["NY"] = "non-evil"
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    public string Render(string name, MonsterFields f)
    {
        var sb = new StringBuilder();

        // Size
        var sizeText = f.Size is { Count: > 0 }
            ? string.Join(", ", f.Size.Select(RendererHelpers.MapSize))
            : "Unknown";

        // Type
        var typeText = ExtractTypeName(f.Type);

        // Alignment
        var alignText = f.Alignment is { Count: > 0 }
            ? string.Join(" ", f.Alignment.Select(a => AlignMap.TryGetValue(a, out var n) ? n : a))
            : "unaligned";

        sb.AppendLine($"{name} — {sizeText} {typeText}, {alignText}");

        // AC
        if (f.Ac is { Count: > 0 })
        {
            var acVal = ExtractAc(f.Ac[0]);
            sb.AppendLine($"AC {acVal}");
        }

        // HP
        if (f.Hp is { } hp)
        {
            var hpFormula = hp.Formula is not null ? $" ({hp.Formula})" : "";
            sb.AppendLine($"HP {hp.Average}{hpFormula}");
        }

        // Speed
        if (f.Speed.HasValue && f.Speed.Value.ValueKind == JsonValueKind.Object)
        {
            var parts = f.Speed.Value.EnumerateObject()
                .Select(p => p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt32(out var n)
                    ? $"{p.Name} {n} ft."
                    : p.Value.ValueKind == JsonValueKind.String
                        ? $"{p.Name} {p.Value.GetString()}"
                        : $"{p.Name} ?");
            sb.AppendLine($"Speed {string.Join(", ", parts)}");
        }

        // Ability scores — emit only the scores that are present so partial stat blocks do not
        // render blank "DEX  INT " gaps (COR-13).
        var abilities = new List<string>(6);
        if (f.Str.HasValue) abilities.Add($"STR {f.Str}");
        if (f.Dex.HasValue) abilities.Add($"DEX {f.Dex}");
        if (f.Con.HasValue) abilities.Add($"CON {f.Con}");
        if (f.Int.HasValue) abilities.Add($"INT {f.Int}");
        if (f.Wis.HasValue) abilities.Add($"WIS {f.Wis}");
        if (f.Cha.HasValue) abilities.Add($"CHA {f.Cha}");
        if (abilities.Count > 0)
            sb.AppendLine(string.Join(" ", abilities));

        // CR
        var crText = ExtractCr(f.Cr);
        sb.AppendLine($"CR {crText}");

        // The embedding reads only the first characters (OllamaEmbeddingService), so everything added after this point
        // keeps the opening, and with it the search vector of a long stat block, as it was.
        AppendBlocks(sb, "Traits:", f.Trait);
        AppendBlocks(sb, "Actions:", f.Action);
        AppendBlocks(sb, "Bonus Actions:", f.Bonus);
        AppendBlocks(sb, "Reactions:", f.Reaction);
        AppendBlocks(sb, "Legendary Actions:", f.Legendary, f.LegendaryHeader);
        AppendSpellcasting(sb, f);
        AppendEntries(sb, "Lair Actions:", f.Lair, f.LairHeader);
        AppendEntries(sb, "Variants:", f.Variant);

        AppendList(sb, "Saving Throws", KeyValues(f.Save), ", ");
        AppendList(sb, "Skills", KeyValues(f.Skill), ", ");
        AppendList(sb, "Damage Vulnerabilities", DamageGroups(f.Vulnerable), "; ");
        AppendList(sb, "Damage Resistances", DamageGroups(f.Resist), "; ");
        AppendList(sb, "Damage Immunities", DamageGroups(f.Immune), "; ");
        AppendList(sb, "Condition Immunities", DamageGroups(f.ConditionImmune), ", ");
        var senses = new List<string>(f.Senses ?? []);
        if (f.Passive is { } passive) senses.Add($"passive Perception {passive}");
        AppendList(sb, "Senses", senses, ", ");
        AppendList(sb, "Languages", f.Languages ?? [], ", ");

        return sb.ToString();
    }

    private static void AppendBlocks(
        StringBuilder sb, string title, IReadOnlyList<MonsterBlock>? blocks, IReadOnlyList<string>? header = null)
    {
        if (blocks is not { Count: > 0 }) return;
        sb.AppendLine(title);
        foreach (var line in header ?? []) sb.AppendLine($"  {RendererHelpers.StripTags(line)}");
        foreach (var b in blocks)
        {
            var text = RendererHelpers.EntriesText(b.Entries);
            if (string.IsNullOrEmpty(b.Name)) sb.AppendLine($"  {text}");
            else sb.AppendLine(text.Length > 0 ? $"  {b.Name}: {text}" : $"  {b.Name}");
        }
    }

    /// <summary>A section whose items are plain text lines or named blocks.</summary>
    private static void AppendEntries(
        StringBuilder sb, string title, IReadOnlyList<JsonElement>? items, IReadOnlyList<string>? header = null)
    {
        var lines = new List<string>();
        foreach (var item in items ?? [])
        {
            var name = item.ValueKind == JsonValueKind.Object ? RendererHelpers.StringProp(item, "name") : string.Empty;
            var text = RendererHelpers.EntriesText(name.Length > 0 && item.TryGetProperty("entries", out var entries) ? [entries] : [item]);
            if (text.Length > 0) lines.Add(name.Length > 0 ? $"  {name}: {text}" : $"  {text}");
        }
        if (lines.Count == 0) return;
        sb.AppendLine(title);
        foreach (var line in header ?? []) sb.AppendLine($"  {RendererHelpers.StripTags(line)}");
        foreach (var line in lines) sb.AppendLine(line);
    }

    private static void AppendList(StringBuilder sb, string label, IEnumerable<string> values, string separator)
    {
        var text = string.Join(separator, values.Where(v => !string.IsNullOrWhiteSpace(v)));
        if (text.Length > 0) sb.AppendLine($"{label}: {text}");
    }

    private static IEnumerable<string> KeyValues(JsonElement? e) =>
        e is { ValueKind: JsonValueKind.Object } o
            ? o.EnumerateObject().Select(p => (p.Name, Value: RendererHelpers.NumberOrStringProp(o, p.Name)))
                .Where(p => p.Value is not null).Select(p => $"{p.Name} {p.Value}")
            : [];

    private static readonly string[] DamageKeys = ["resist", "immune", "vulnerable", "conditionImmune"];

    /// <summary>"cold" or { "resist": ["bludgeoning", ...], "preNote": ..., "note": "from nonmagical attacks" }.</summary>
    private static IEnumerable<string> DamageGroups(IReadOnlyList<JsonElement>? items)
    {
        foreach (var item in items ?? [])
        {
            if (item.ValueKind == JsonValueKind.String) yield return item.GetString()!;
            if (item.ValueKind != JsonValueKind.Object) continue;
            var kinds = DamageKeys
                .SelectMany(k => item.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Array
                    ? DamageGroups([.. v.EnumerateArray()]) : [])
                .ToList();
            string[] parts = [RendererHelpers.StringProp(item, "special"), RendererHelpers.StringProp(item, "preNote"),
                string.Join(", ", kinds), RendererHelpers.StringProp(item, "note")];
            yield return string.Join(' ', parts.Where(p => p.Length > 0));
        }
    }

    private static void AppendSpellcasting(StringBuilder sb, MonsterFields f)
    {
        // Entries the extractor already printed under Traits/Actions must not be repeated.
        var printed = new[] { f.Trait, f.Action, f.Bonus, f.Reaction }
            .SelectMany(b => b ?? []).Select(b => b.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var s in f.Spellcasting ?? [])
        {
            if (s.ValueKind != JsonValueKind.Object) continue;
            var name = RendererHelpers.StringProp(s, "name");
            if (name.Length == 0) name = "Spellcasting";
            if (printed.Contains(name)) continue;

            var parts = new List<string>();
            if (s.TryGetProperty("headerEntries", out var header)) AddText(parts, RendererHelpers.EntriesText([header]), "");
            if (s.TryGetProperty("will", out var will)) AddText(parts, $"At will: {SpellList(will)}", "At will: ");
            foreach (var (key, unit) in new[] { ("daily", "day"), ("rest", "rest"), ("weekly", "week") })
                if (s.TryGetProperty(key, out var uses) && uses.ValueKind == JsonValueKind.Object)
                    foreach (var use in uses.EnumerateObject())
                    {
                        var label = use.Name.EndsWith('e') ? $"{use.Name[..^1]}/{unit} each: " : $"{use.Name}/{unit}: ";
                        AddText(parts, label + SpellList(use.Value), label);
                    }
            if (s.TryGetProperty("spells", out var levels) && levels.ValueKind == JsonValueKind.Object)
                foreach (var level in levels.EnumerateObject())
                {
                    if (level.Value.ValueKind != JsonValueKind.Object || !level.Value.TryGetProperty("spells", out var spells)) continue;
                    var label = LevelLabel(level.Name, RendererHelpers.IntProp(level.Value, "slots"));
                    AddText(parts, label + SpellList(spells), label);
                }
            if (s.TryGetProperty("footerEntries", out var footer)) AddText(parts, RendererHelpers.EntriesText([footer]), "");
            if (parts.Count > 0) sb.AppendLine($"{name}: {string.Join(' ', parts)}");
        }
    }

    private static string SpellList(JsonElement spells) =>
        spells.ValueKind == JsonValueKind.Array
            ? string.Join(", ", spells.EnumerateArray().Select(x => RendererHelpers.EntriesText([x])).Where(x => x.Length > 0))
            : string.Empty;

    private static string LevelLabel(string level, int? slots)
    {
        if (level == "0") return "Cantrips: ";
        var ordinal = level switch { "1" => "1st", "2" => "2nd", "3" => "3rd", _ => level + "th" };
        return slots is { } n ? $"{ordinal} level ({n} {(n == 1 ? "slot" : "slots")}): " : $"{ordinal} level: ";
    }

    private static void AddText(List<string> parts, string text, string label)
    {
        if (text.Length <= label.Length) return;
        parts.Add(text[^1] is '.' or ':' or '!' or '?' ? text : text + ".");
    }

    private static string ExtractTypeName(JsonElement? e)
    {
        if (e is null || e.Value.ValueKind == JsonValueKind.Undefined) return "unknown";
        if (e.Value.ValueKind == JsonValueKind.String)
            return e.Value.GetString() ?? "unknown";
        if (e.Value.ValueKind == JsonValueKind.Object && e.Value.TryGetProperty("type", out var t)
            && t.ValueKind == JsonValueKind.String)
            return t.GetString() ?? "unknown";
        return "unknown";
    }

    private static string ExtractAc(JsonElement ac)
    {
        if (ac.ValueKind == JsonValueKind.Number && ac.TryGetInt32(out var n))
            return n.ToString();
        if (ac.ValueKind == JsonValueKind.Object && ac.TryGetProperty("ac", out var acProp)
            && acProp.ValueKind == JsonValueKind.Number && acProp.TryGetInt32(out var acN))
            return acN.ToString();
        return "?";
    }

    private static string ExtractCr(JsonElement? e)
    {
        if (e is null || e.Value.ValueKind == JsonValueKind.Undefined) return "?";
        if (e.Value.ValueKind == JsonValueKind.String)
            return e.Value.GetString() ?? "?";
        if (e.Value.ValueKind == JsonValueKind.Object)
            return RendererHelpers.NumberOrStringProp(e.Value, "cr") ?? "?";
        return "?";
    }
}