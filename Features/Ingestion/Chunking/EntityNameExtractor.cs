namespace DndMcpAICsharpFun.Features.Ingestion.Chunking;

public static class EntityNameExtractor
{
    public static string? Extract(IReadOnlyList<string> lines, int boundaryIndex)
    {
        // The entity name is typically the heading on the line just before the boundary anchor
        for (int i = boundaryIndex - 1; i >= 0 && i >= boundaryIndex - 3; i--)
        {
            var candidate = lines[i].Trim();
            if (candidate.Length > 0 && candidate.Length <= 100)
                return candidate;
        }
        return null;
    }
}
