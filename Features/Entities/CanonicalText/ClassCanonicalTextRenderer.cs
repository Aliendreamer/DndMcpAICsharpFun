using System.Text;

using DndMcpAICsharpFun.Domain.Entities.Fields;

namespace DndMcpAICsharpFun.Features.Entities.CanonicalText;

public sealed class ClassCanonicalTextRenderer
{
    public string Render(string name, ClassFields f)
    {
        var sb = new StringBuilder();
        var hitDie = f.Hd is { } hd ? $"d{hd.Faces}" : "?";
        sb.AppendLine($"{name} — {hitDie} hit die.");

        if (f.Proficiency is { Count: > 0 })
        {
            var saves = string.Join(", ", f.Proficiency.Where(p => p is not null).Select(p => p.ToUpperInvariant()));
            if (saves.Length > 0)
                sb.AppendLine($"Saving throws: {saves}.");
        }

        if (f.ClassFeatures is { Count: > 0 })
        {
            var features = f.ClassFeatures
                .Select(e => RendererHelpers.ExtractFeatureEntry(e, "classFeature", minParts: 3))
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