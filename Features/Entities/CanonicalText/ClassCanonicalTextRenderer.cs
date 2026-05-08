using System.Text;
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities.Fields;

namespace DndMcpAICsharpFun.Features.Entities.CanonicalText;

public sealed class ClassCanonicalTextRenderer : IEntityCanonicalTextRenderer<ClassFields>
{
    public string Render(string name, ClassFields f)
    {
        var sb = new StringBuilder();
        var hitDie = f.Hd is { } hd ? $"d{hd.Faces}" : "?";
        sb.AppendLine($"{name} — {hitDie} hit die.");

        if (f.Proficiency is { Count: > 0 })
        {
            var saves = string.Join(", ", f.Proficiency.Select(p => p.ToUpperInvariant()));
            sb.AppendLine($"Saving throws: {saves}.");
        }

        if (f.ClassFeatures is { Count: > 0 })
        {
            var features = f.ClassFeatures
                .Select(ExtractFeatureEntry)
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .OrderBy(x => x.Level)
                .Select(x => $"{x.Name} ({x.Level})")
                .ToList();
            if (features.Count > 0)
                sb.AppendLine($"Features: {string.Join(", ", features)}.");
        }

        return sb.ToString();
    }

    private static (string Name, int Level)? ExtractFeatureEntry(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.String)
        {
            var raw = e.GetString() ?? string.Empty;
            var parts = raw.Split('|');
            if (parts.Length >= 3 && int.TryParse(parts[^1], out var lv))
                return (StripPipe(parts[0]), lv);
            return null;
        }
        if (e.ValueKind == JsonValueKind.Object
            && e.TryGetProperty("classFeature", out var cf)
            && e.TryGetProperty("level", out var lv2))
        {
            var raw = cf.GetString() ?? string.Empty;
            var name = StripPipe(raw.Split('|')[0]);
            return (name, lv2.GetInt32());
        }
        return null;
    }

    private static string StripPipe(string s)
    {
        var idx = s.IndexOf('|');
        return idx >= 0 ? s[..idx] : s;
    }
}
