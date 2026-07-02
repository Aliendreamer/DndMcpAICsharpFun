using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DndMcpAICsharpFun.Domain.Entities.Fields;

namespace DndMcpAICsharpFun.Features.Entities.CanonicalText;

public sealed class MonsterCanonicalTextRenderer : IEntityCanonicalTextRenderer<MonsterFields>
{
    private static readonly Regex TagRx = new(@"\{@\w+\s([^|}]+)[^}]*\}", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> SizeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["T"] = "Tiny", ["S"] = "Small", ["M"] = "Medium",
        ["L"] = "Large", ["H"] = "Huge", ["G"] = "Gargantuan"
    };

    private static readonly Dictionary<string, string> AlignMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["L"] = "Lawful", ["C"] = "Chaotic", ["G"] = "Good", ["E"] = "Evil",
        ["N"] = "Neutral", ["U"] = "Unaligned", ["A"] = "Any alignment",
        ["NX"] = "non-chaotic", ["NY"] = "non-evil"
    };

    public string Render(string name, MonsterFields f)
    {
        var sb = new StringBuilder();

        // Size
        var sizeText = f.Size is { Count: > 0 }
            ? string.Join(", ", f.Size.Select(s => SizeMap.TryGetValue(s, out var n) ? n : s))
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

        // Traits
        if (f.Trait is { Count: > 0 })
        {
            sb.AppendLine("Traits:");
            foreach (var t in f.Trait)
            {
                var entryText = GetFirstEntryText(t.Entries);
                sb.AppendLine(entryText.Length > 0 ? $"  {t.Name}: {entryText}" : $"  {t.Name}");
            }
        }

        // Actions
        if (f.Action is { Count: > 0 })
        {
            sb.AppendLine("Actions:");
            foreach (var a in f.Action)
            {
                var entryText = GetFirstEntryText(a.Entries);
                sb.AppendLine(entryText.Length > 0 ? $"  {a.Name}: {entryText}" : $"  {a.Name}");
            }
        }

        // Legendary
        if (f.Legendary is { Count: > 0 })
        {
            foreach (var l in f.Legendary)
                sb.AppendLine($"Legendary — {l.Name}");
        }

        return sb.ToString();
    }

    private static string ExtractTypeName(JsonElement? e)
    {
        if (e is null || e.Value.ValueKind == JsonValueKind.Undefined) return "unknown";
        if (e.Value.ValueKind == JsonValueKind.String)
            return e.Value.GetString() ?? "unknown";
        if (e.Value.ValueKind == JsonValueKind.Object && e.Value.TryGetProperty("type", out var t))
            return t.GetString() ?? "unknown";
        return "unknown";
    }

    private static string ExtractAc(JsonElement ac)
    {
        if (ac.ValueKind == JsonValueKind.Number && ac.TryGetInt32(out var n))
            return n.ToString();
        if (ac.ValueKind == JsonValueKind.Object && ac.TryGetProperty("ac", out var acProp)
            && acProp.TryGetInt32(out var acN))
            return acN.ToString();
        return "?";
    }

    private static string ExtractCr(JsonElement? e)
    {
        if (e is null || e.Value.ValueKind == JsonValueKind.Undefined) return "?";
        if (e.Value.ValueKind == JsonValueKind.String)
            return e.Value.GetString() ?? "?";
        if (e.Value.ValueKind == JsonValueKind.Object && e.Value.TryGetProperty("cr", out var crProp))
            return crProp.GetString() ?? "?";
        return "?";
    }

    private static string GetFirstEntryText(IReadOnlyList<JsonElement>? entries)
    {
        if (entries is null or { Count: 0 }) return string.Empty;
        var first = entries[0];
        return first.ValueKind == JsonValueKind.String ? StripTags(first.GetString()!) : string.Empty;
    }

    private static string StripTags(string s) => TagRx.Replace(s, "$1");
}
