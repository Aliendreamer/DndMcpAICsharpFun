using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Ingestion.Chunking.Detectors;

namespace DndMcpAICsharpFun.Features.Ingestion.Chunking;

public sealed class ContentCategoryDetector(IEnumerable<IPatternDetector> detectors)
{
    private const float ConfidenceThreshold = 0.7f;

    private readonly IReadOnlyList<IPatternDetector> _detectors = [.. detectors];

    public ContentCategory Detect(string chunkText, ContentCategory chapterDefault)
    {
        ContentCategory best = chapterDefault;
        float bestScore = 0f;

        foreach (var detector in _detectors)
        {
            float score = detector.Detect(chunkText);
            if (score >= ConfidenceThreshold && score > bestScore)
            {
                bestScore = score;
                best = detector.Category;
            }
        }

        return best;
    }

    public IPatternDetector? FindBoundaryDetector(string line)
    {
        foreach (var detector in _detectors)
        {
            if (detector.IsEntityBoundary(line))
                return detector;
        }
        return null;
    }
}
