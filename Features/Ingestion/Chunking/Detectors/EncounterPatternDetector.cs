using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Chunking.Detectors;

public sealed class EncounterPatternDetector : IPatternDetector
{
    public ContentCategory Category => ContentCategory.Encounter;

    public float Detect(string text)
    {
        int hits = 0;
        if (text.Contains("Encounter Difficulty", StringComparison.OrdinalIgnoreCase)) hits++;
        if (text.Contains("XP Threshold", StringComparison.OrdinalIgnoreCase)) hits++;
        if (text.Contains("Random Encounter", StringComparison.OrdinalIgnoreCase)) hits++;
        return hits / 3f;
    }

    public bool IsEntityBoundary(string line) =>
        line.Contains("Encounter Difficulty", StringComparison.OrdinalIgnoreCase) ||
        line.Contains("Random Encounter", StringComparison.OrdinalIgnoreCase);
}
