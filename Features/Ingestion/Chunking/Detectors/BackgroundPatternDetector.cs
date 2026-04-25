using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Chunking.Detectors;

public sealed class BackgroundPatternDetector : IPatternDetector
{
    public ContentCategory Category => ContentCategory.Background;

    public float Detect(string text)
    {
        int hits = 0;
        if (text.Contains("Skill Proficiencies:", StringComparison.OrdinalIgnoreCase)) hits++;
        if (text.Contains("Feature:", StringComparison.OrdinalIgnoreCase)) hits++;
        return hits / 2f;
    }

    public bool IsEntityBoundary(string line) =>
        line.Contains("Skill Proficiencies:", StringComparison.OrdinalIgnoreCase);
}
