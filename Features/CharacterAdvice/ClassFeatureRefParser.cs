using System.Text.Json;

namespace DndMcpAICsharpFun.Features.CharacterAdvice;

public sealed record FeatureRef(string Name, string Source, int Level);

/// <summary>Parses 5etools classFeatures/subclassFeatures refs ("Name|Class|Source|Level",
/// string or { key: "..." } object) into (name, source, level). Malformed refs are skipped.</summary>
public static class ClassFeatureRefParser
{
    public static IReadOnlyList<FeatureRef> Parse(IReadOnlyList<JsonElement>? refs, string key)
    {
        if (refs is null) return [];
        var result = new List<FeatureRef>();
        foreach (var el in refs)
        {
            var raw = el.ValueKind switch
            {
                JsonValueKind.String => el.GetString(),
                JsonValueKind.Object when el.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String
                    => p.GetString(),
                _ => null,
            };
            if (raw is null) continue;

            var parts = raw.Split('|');
            // 4-part classFeature refs: Name|Class|Source|Level
            // 6-part subclassFeature refs: Name|ClassName|ClassSource|SubclassShortName|SubclassSource|Level
            // Level is always the last part, Source is always the second-to-last part, for both arities.
            if (parts.Length < 4) continue;
            if (!int.TryParse(parts[^1].Trim(), out var level)) continue;
            var name = parts[0].Trim();
            var source = parts[^2].Trim();
            if (name.Length == 0) continue;
            result.Add(new FeatureRef(name, source, level));
        }
        return result;
    }
}
