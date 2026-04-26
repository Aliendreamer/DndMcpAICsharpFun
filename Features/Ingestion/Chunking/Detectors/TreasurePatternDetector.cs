using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Chunking.Detectors;

public sealed class TreasurePatternDetector : IPatternDetector
{
    public ContentCategory Category => ContentCategory.Treasure;

    public float Detect(string text)
    {
        int hits = 0;
        if (text.Contains("Treasure Hoard", StringComparison.OrdinalIgnoreCase)) hits++;
        if (text.Contains("Art Objects", StringComparison.OrdinalIgnoreCase)) hits++;
        if (text.Contains("Gemstones", StringComparison.OrdinalIgnoreCase)) hits++;
        return hits / 3f;
    }

    public bool IsEntityBoundary(string line) =>
        line.Contains("Treasure Hoard", StringComparison.OrdinalIgnoreCase);
}
