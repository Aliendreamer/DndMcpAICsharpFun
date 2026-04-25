using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Chunking.Detectors;

public sealed class SpellPatternDetector : IPatternDetector
{
    public ContentCategory Category => ContentCategory.Spell;

    public float Detect(string text)
    {
        int hits = 0;
        if (text.Contains("Casting Time:", StringComparison.OrdinalIgnoreCase)) hits++;
        if (text.Contains("Range:", StringComparison.OrdinalIgnoreCase)) hits++;
        if (text.Contains("Components:", StringComparison.OrdinalIgnoreCase)) hits++;
        if (text.Contains("Duration:", StringComparison.OrdinalIgnoreCase)) hits++;
        return hits / 4f;
    }

    public bool IsEntityBoundary(string line) =>
        line.Contains("Casting Time:", StringComparison.OrdinalIgnoreCase);
}
