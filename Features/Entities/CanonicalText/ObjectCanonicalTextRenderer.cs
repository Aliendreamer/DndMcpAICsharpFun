using System.Text;
using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities.Fields;

namespace DndMcpAICsharpFun.Features.Entities.CanonicalText;

/// <summary>
/// Renders an <see cref="Domain.Entities.EntityType.Object"/> entity (siege weapons, animated
/// objects, etc.) to canonical text: Armor Class, Hit Points, immunities/resistances, and attack
/// actions. A trimmed cousin of <see cref="MonsterCanonicalTextRenderer"/> — objects have no
/// ability scores, CR, speed, or legendary/lair actions.
/// </summary>
public sealed class ObjectCanonicalTextRenderer
{
    public string Render(string name, ObjectFields f)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{name} — Object");

        if (f.Ac is { Count: > 0 })
            sb.AppendLine($"AC {ExtractAc(f.Ac[0])}");

        if (f.Hp is { } hp)
        {
            var hpFormula = hp.Formula is not null ? $" ({hp.Formula})" : "";
            sb.AppendLine($"HP {hp.Average}{hpFormula}");
        }

        AppendList(sb, "Damage Immunities", f.Immune);
        AppendList(sb, "Damage Resistances", f.Resist);
        AppendList(sb, "Damage Vulnerabilities", f.Vulnerable);
        AppendList(sb, "Condition Immunities", f.ConditionImmune);

        if (f.Action is { Count: > 0 })
        {
            sb.AppendLine("Actions:");
            foreach (var a in f.Action)
            {
                var entryText = GetFirstEntryText(a.Entries);
                sb.AppendLine(entryText.Length > 0 ? $"  {a.Name}: {entryText}" : $"  {a.Name}");
            }
        }

        if (!string.IsNullOrWhiteSpace(f.Description))
            sb.AppendLine(RendererHelpers.StripTags(f.Description));

        return sb.ToString();
    }

    private static void AppendList(StringBuilder sb, string label, IReadOnlyList<JsonElement>? items)
    {
        if (items is null or { Count: 0 }) return;
        var parts = items
            .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
            .Where(s => !string.IsNullOrWhiteSpace(s));
        var joined = string.Join(", ", parts);
        if (joined.Length > 0)
            sb.AppendLine($"{label}: {joined}");
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

    private static string GetFirstEntryText(IReadOnlyList<JsonElement>? entries)
    {
        if (entries is null or { Count: 0 }) return string.Empty;
        var first = entries[0];
        return first.ValueKind == JsonValueKind.String ? RendererHelpers.StripTags(first.GetString()!) : string.Empty;
    }
}