using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Chunking.Detectors;

public interface IPatternDetector
{
    ContentCategory Category { get; }
    float Detect(string text);
    bool IsEntityBoundary(string line);
}
