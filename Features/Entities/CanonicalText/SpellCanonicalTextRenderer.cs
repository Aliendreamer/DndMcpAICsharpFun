using System.Text;
using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities.Fields;

namespace DndMcpAICsharpFun.Features.Entities.CanonicalText;

public sealed class SpellCanonicalTextRenderer
{
    public string Render(string name, SpellFields f)
    {
        var sb = new StringBuilder();
        var schoolDisplay = (f.School ?? "").ToUpperInvariant() switch
        {
            "A" => "Abjuration",
            "C" => "Conjuration",
            "D" => "Divination",
            "E" => "Enchantment",
            "I" => "Illusion",
            "N" => "Necromancy",
            "T" => "Transmutation",
            "V" => "Evocation",
            _ => f.School
        };
        var levelText = f.Level == 0
            ? $"{schoolDisplay} cantrip"
            : $"{Ordinal(f.Level)}-level {schoolDisplay}";
        sb.AppendLine($"{name} — {levelText}");
        if (f.Time is { Count: > 0 })
            sb.AppendLine($"Casting Time: {f.Time[0].Number} {f.Time[0].Unit}");
        if (f.Range?.Distance?.Amount is { } amt)
            sb.AppendLine($"Range: {amt} {f.Range.Distance.Type}");
        else if (f.Range != null)
            sb.AppendLine($"Range: {f.Range.Type}");
        if (f.Components != null)
        {
            var comps = new List<string>();
            if (f.Components.V) comps.Add("V");
            if (f.Components.S) comps.Add("S");
            if (f.Components.M.HasValue)
            {
                var m = f.Components.M.Value;
                var matText = m.ValueKind == JsonValueKind.String ? m.GetString()
                    : m.ValueKind == JsonValueKind.Object && m.TryGetProperty("text", out var t) ? t.GetString()
                    : null;
                comps.Add(matText is null ? "M" : $"M ({matText})");
            }
            sb.AppendLine($"Components: {string.Join(", ", comps)}");
        }
        if (f.Duration is { Count: > 0 })
        {
            var d = f.Duration[0];
            var durText = d.Type switch
            {
                "instant" => "Instantaneous",
                "permanent" => "Until dispelled",
                "timed" when d.Duration.HasValue
                    && d.Duration.Value.TryGetProperty("amount", out var da)
                    && d.Duration.Value.TryGetProperty("type", out var dt)
                    => $"{da.GetInt32()} {dt.GetString()}",
                _ => d.Type
            };
            sb.AppendLine($"Duration: {(f.Concentration ? "Concentration, up to " : "")}{durText}");
        }
        if (f.Classes is { Count: > 0 })
            sb.AppendLine($"Classes: {string.Join(", ", f.Classes)}");
        sb.AppendLine();
        if (f.Entries != null)
            foreach (var e in f.Entries)
                AppendEntry(sb, e);
        if (f.EntriesHigherLevel is { Count: > 0 })
        {
            sb.AppendLine();
            sb.Append("At Higher Levels: ");
            foreach (var e in f.EntriesHigherLevel)
                AppendEntry(sb, e);
        }
        return sb.ToString();
    }

    private static void AppendEntry(StringBuilder sb, JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.String)
            sb.AppendLine(RendererHelpers.StripTags(e.GetString()!));
        else if (e.ValueKind == JsonValueKind.Object)
        {
            if (e.TryGetProperty("entries", out var entries))
                foreach (var child in entries.EnumerateArray())
                    AppendEntry(sb, child);
            else if (e.TryGetProperty("items", out var items))
                foreach (var child in items.EnumerateArray())
                    AppendEntry(sb, child);
        }
    }

    private static string Ordinal(int n) => n switch
    {
        1 => "1st",
        2 => "2nd",
        3 => "3rd",
        _ => $"{n}th"
    };
}