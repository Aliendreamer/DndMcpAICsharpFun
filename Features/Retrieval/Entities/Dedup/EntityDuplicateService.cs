using DndMcpAICsharpFun.Features.VectorStore.Entities;

namespace DndMcpAICsharpFun.Features.Retrieval.Entities.Dedup;

/// <summary>Scans the whole entity corpus for duplicates (by <see cref="EntityDedupKey"/>) and
/// can compact them by deleting the losers as chosen by <see cref="DuplicateResolver"/>.</summary>
public sealed class EntityDuplicateService(IEntityVectorStore store, IBookTypeLookup bookTypeLookup)
{
    public async Task<DuplicateReport> FindDuplicatesAsync(CancellationToken ct = default)
    {
        var (groups, _) = await BuildGroupsAsync(ct);
        return Report(groups);
    }

    public async Task<DuplicateReport> CompactAsync(bool apply, CancellationToken ct = default)
    {
        var (groups, loserIds) = await BuildGroupsAsync(ct);
        if (apply && loserIds.Count > 0)
            await store.DeleteByIdsAsync(loserIds, ct);
        return Report(groups);
    }

    private async Task<(List<DuplicateGroup> groups, List<string> loserIds)> BuildGroupsAsync(CancellationToken ct)
    {
        var hits = await store.ScrollAllAsync(ct);
        var bookTypes = await bookTypeLookup.BuildAsync(ct);
        var groups = new List<DuplicateGroup>();
        var loserIds = new List<string>();

        foreach (var g in hits.GroupBy(h => EntityDedupKey.From(h.Envelope)))
        {
            var members = g.ToList();
            if (members.Count < 2) continue;
            var winner = DuplicateResolver.Winner(members.Select(h => h.Envelope).ToList(), bookTypes);
            var losers = members.Where(h => h.Envelope.Id != winner.Id).Select(h => h.Envelope.Id).ToList();
            loserIds.AddRange(losers);
            groups.Add(new DuplicateGroup(
                $"{g.Key.NormalizedName}|{g.Key.Type}|{g.Key.Edition}", winner.Id, losers));
        }
        return (groups, loserIds);
    }

    private static DuplicateReport Report(List<DuplicateGroup> groups) =>
        new(groups.Count, groups.Sum(g => g.LoserIds.Count), groups);
}