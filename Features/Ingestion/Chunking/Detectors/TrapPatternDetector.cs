using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Chunking.Detectors;

public sealed class TrapPatternDetector : IPatternDetector
{
    public ContentCategory Category => ContentCategory.Trap;

    public float Detect(string text)
    {
        int hits = 0;
        if (text.Contains("Trigger:", StringComparison.OrdinalIgnoreCase)) hits++;
        if (text.Contains("Effect:", StringComparison.OrdinalIgnoreCase)) hits++;
        if (text.Contains("Disarm DC:", StringComparison.OrdinalIgnoreCase)) hits++;
        return hits / 3f;
    }

    public bool IsEntityBoundary(string line) =>
        line.Contains("Trigger:", StringComparison.OrdinalIgnoreCase);
}
