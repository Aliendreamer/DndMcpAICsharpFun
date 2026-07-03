using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

public readonly record struct MonsterNameCleanupCounts(
    int Cleaned, int Deduped, int GroundedCollisionsFlagged, int Grounded, int Backfilled);

/// <summary>
/// One-time, pure transform that rewrites stat-line-garbled canonical Monster names to their clean
/// 5etools canonical form and de-duplicates the resulting duplicate 5etools-backfill entities.
/// Reuses the extractor's <see cref="EntityNameMatcher"/> (stat-line stripping) and
/// <see cref="EntityIdSlug"/> so its output is identical to what a re-extract would produce.
/// </summary>
public static class MonsterNameCleanup
{
    private const string BackfillDataSource = "5etools-backfill";

    private static bool IsBackfill(EntityEnvelope e) =>
        string.Equals(e.DataSource, BackfillDataSource, StringComparison.Ordinal);

    public static (IReadOnlyList<EntityEnvelope> Entities, MonsterNameCleanupCounts Counts) Clean(
        IReadOnlyList<EntityEnvelope> entities, EntityNameMatcher matcher, string bookKey)
    {
        // 1. Rewrite garbled monster names + recompute ids (all other fields preserved).
        var cleaned = 0;
        var rewritten = new List<EntityEnvelope>(entities.Count);
        foreach (var e in entities)
        {
            if (e.Type != EntityType.Monster)
            {
                rewritten.Add(e);
                continue;
            }

            if (matcher.MatchOfType(e.Name, EntityType.Monster) is { } m
                && !string.Equals(m.Canonical, e.Name, StringComparison.Ordinal))
            {
                cleaned++;
                rewritten.Add(e with
                {
                    Name = m.Canonical,
                    Id = EntityIdSlug.For(bookKey, EntityType.Monster, m.Canonical),
                });
            }
            else
            {
                rewritten.Add(e);
            }
        }

        // 2. De-dupe by normalized monster name: keep grounded, drop backfill; flag grounded-vs-grounded.
        var deduped = 0;
        var groundedCollisionsFlagged = 0;
        var drop = new HashSet<int>();
        var flag = new HashSet<int>();

        var monsterGroups = rewritten
            .Select((e, i) => (e, i))
            .Where(x => x.e.Type == EntityType.Monster)
            .GroupBy(x => EntityNameIndex.Normalize(x.e.Name), StringComparer.Ordinal);

        foreach (var group in monsterGroups)
        {
            var grounded = group.Where(x => !IsBackfill(x.e)).Select(x => x.i).ToList();
            var backfill = group.Where(x => IsBackfill(x.e)).Select(x => x.i).ToList();

            if (grounded.Count >= 1 && backfill.Count >= 1)
            {
                foreach (var bi in backfill) drop.Add(bi);
                deduped += backfill.Count;
            }
            if (grounded.Count >= 2)
            {
                foreach (var gi in grounded.Skip(1)) flag.Add(gi);
                groundedCollisionsFlagged += grounded.Count - 1;
            }
        }

        var result = new List<EntityEnvelope>(rewritten.Count);
        for (var i = 0; i < rewritten.Count; i++)
        {
            if (drop.Contains(i)) continue;
            var e = rewritten[i];
            if (flag.Contains(i) && !e.NeedsReview) e = e with { NeedsReview = true };
            result.Add(e);
        }

        var groundedCount = result.Count(e => e.Type == EntityType.Monster && !IsBackfill(e));
        var backfilledCount = result.Count(e => e.Type == EntityType.Monster && IsBackfill(e));

        return (result, new MonsterNameCleanupCounts(
            cleaned, deduped, groundedCollisionsFlagged, groundedCount, backfilledCount));
    }
}
