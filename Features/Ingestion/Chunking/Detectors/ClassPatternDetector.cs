using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Chunking.Detectors;

public sealed class ClassPatternDetector : IPatternDetector
{
    public ContentCategory Category => ContentCategory.Class;

    public float Detect(string text)
    {
        int hits = 0;
        if (text.Contains("Hit Dice:", StringComparison.OrdinalIgnoreCase)) hits++;
        if (text.Contains("Proficiencies:", StringComparison.OrdinalIgnoreCase)) hits++;
        if (text.Contains("Saving Throws:", StringComparison.OrdinalIgnoreCase)) hits++;
        return hits / 3f;
    }

    public bool IsEntityBoundary(string line) =>
        line.Contains("Hit Dice:", StringComparison.OrdinalIgnoreCase);
}
