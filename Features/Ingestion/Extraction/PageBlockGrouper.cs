using DndMcpAICsharpFun.Domain;

namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public static class PageBlockGrouper
{
    public static IReadOnlyList<IReadOnlyList<PageBlock>> Group(IReadOnlyList<PageBlock> blocks)
    {
        var groups = new List<List<PageBlock>>();
        List<PageBlock>? current = null;

        foreach (var block in blocks)
        {
            if (block.Level is "h1" or "h2")
            {
                current = [block];
                groups.Add(current);
            }
            else
            {
                if (current is null)
                {
                    current = [];
                    groups.Add(current);
                }
                current.Add(block);
            }
        }

        return groups;
    }
}
