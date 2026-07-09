using System.Text;

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
                .Select(e => RendererHelpers.ExtractFeatureEntry(e, "subclassFeature", minParts: 2))
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


}