using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.VectorStore.Entities;

namespace DndMcpAICsharpFun.Features.Retrieval.Entities.Dedup;

/// <summary>Collapses duplicate entity search hits by dedup key before fusion/rerank.</summary>
public static class EntityHitCollapser
{
    public static IReadOnlyList<EntitySearchHit> Collapse(
        IReadOnlyList<EntitySearchHit> hits,
        IReadOnlyDictionary<string, BookType> bookTypes)
    {
        if (hits.Count <= 1) return hits;

        return hits
            .GroupBy(h => EntityDedupKey.From(h.Envelope))
            .Select(g =>
            {
                var group = g.ToList();
                if (group.Count == 1) return group[0];
                var winner = DuplicateResolver.Winner(group.Select(h => h.Envelope).ToList(), bookTypes);
                var maxScore = group.Max(h => h.Score);
                var winnerHit = group.First(h => h.Envelope.Id == winner.Id); // Id is unique within a group
                return winnerHit with { Score = maxScore };
            })
            .ToList();
    }
}