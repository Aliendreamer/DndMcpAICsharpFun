using System.Text;
using System.Text.Json;
using DndMcpAICsharpFun.Domain.Entities.Fields;

namespace DndMcpAICsharpFun.Features.Entities.CanonicalText;

public sealed class SubclassCanonicalTextRenderer
{
    public string Render(string name, SubclassFields f)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{name} ({f.ClassName} subclass).");

        if (f.SubclassFeatures is { Count: > 0 })
        {
            var features = f.SubclassFeatures
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
            if (parts.Length >= 2 && int.TryParse(parts[^1], out var lv))
                return (parts[0], lv);
            return null;
        }
        if (e.ValueKind == JsonValueKind.Object
            && e.TryGetProperty("subclassFeature", out var sf)
            && e.TryGetProperty("level", out var lv2))
        {
            var raw = sf.GetString() ?? string.Empty;
            return (raw.Split('|')[0], lv2.GetInt32());
        }
        return null;
    }
}
