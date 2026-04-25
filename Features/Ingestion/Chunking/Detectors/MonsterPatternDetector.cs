using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Chunking.Detectors;

public sealed class MonsterPatternDetector : IPatternDetector
{
    public ContentCategory Category => ContentCategory.Monster;

    public float Detect(string text)
    {
        int hits = 0;
        if (text.Contains("Armor Class:", StringComparison.OrdinalIgnoreCase)) hits++;
        if (text.Contains("Hit Points:", StringComparison.OrdinalIgnoreCase)) hits++;
        if (text.Contains("Speed:", StringComparison.OrdinalIgnoreCase)) hits++;
        return hits / 3f;
    }

    public bool IsEntityBoundary(string line) =>
        line.Contains("Armor Class:", StringComparison.OrdinalIgnoreCase);
}
