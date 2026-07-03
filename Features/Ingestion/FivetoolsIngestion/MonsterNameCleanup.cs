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
        string ResolveCanonicalName(string name) =>
            matcher.MatchOfType(name, EntityType.Monster) is { } m ? m.Canonical : name;

        var cleaned = 0;
        var deduped = 0;
        var groundedCollisionsFlagged = 0;

        var result = new EntityEnvelope?[entities.Count];
        var drop = new HashSet<int>();

        var monsterGroups = entities
            .Select((e, i) => (e, i))
            .Where(x => x.e.Type == EntityType.Monster)
            .GroupBy(x => EntityNameIndex.Normalize(ResolveCanonicalName(x.e.Name)), StringComparer.Ordinal);

        foreach (var group in monsterGroups)
        {
            var members = group.ToList();
            var grounded = members.Where(x => !IsBackfill(x.e)).ToList();
            var backfill = members.Where(x => IsBackfill(x.e)).ToList();

            if (grounded.Count >= 1 && backfill.Count >= 1)
            {
                foreach (var (_, i) in backfill) drop.Add(i);
                deduped += backfill.Count;
            }

            if (grounded.Count >= 2)
            {
                // Grounded-vs-grounded collision: pick a single winner to hold the canonical
                // name/id, and leave every other grounded entity's original name/id untouched
                // (only flagging it) so the output never carries duplicate ids.
                var canonicalName = ResolveCanonicalName(grounded[0].e.Name);
                var canonicalId = EntityIdSlug.For(bookKey, EntityType.Monster, canonicalName);

                var winnerIndex = -1;
                foreach (var (e, i) in grounded)
                {
                    if (string.Equals(e.Id, canonicalId, StringComparison.Ordinal))
                    {
                        winnerIndex = i;
                        break;
                    }
                }
                if (winnerIndex == -1) winnerIndex = grounded[0].i;

                foreach (var (e, i) in grounded)
                {
                    if (i == winnerIndex)
                    {
                        if (!string.Equals(e.Name, canonicalName, StringComparison.Ordinal)) cleaned++;
                        result[i] = e with { Name = canonicalName, Id = canonicalId };
                    }
                    else
                    {
                        groundedCollisionsFlagged++;
                        result[i] = e.NeedsReview ? e : e with { NeedsReview = true };
                    }
                }
            }
            else
            {
                // 0 or 1 grounded entity in this group: no collision, just rewrite garbled
                // names to their clean canonical form (backfill members that survive dedupe,
                // i.e. groups with no grounded counterpart, are rewritten the same way).
                foreach (var (e, i) in members)
                {
                    if (drop.Contains(i)) continue;
                    if (matcher.MatchOfType(e.Name, EntityType.Monster) is { } m
                        && !string.Equals(m.Canonical, e.Name, StringComparison.Ordinal))
                    {
                        cleaned++;
                        result[i] = e with
                        {
                            Name = m.Canonical,
                            Id = EntityIdSlug.For(bookKey, EntityType.Monster, m.Canonical),
                        };
                    }
                    else
                    {
                        result[i] = e;
                    }
                }
            }
        }

        var final = new List<EntityEnvelope>(entities.Count);
        for (var i = 0; i < entities.Count; i++)
        {
            if (drop.Contains(i)) continue;
            final.Add(entities[i].Type == EntityType.Monster ? result[i]! : entities[i]);
        }

        var groundedCount = final.Count(e => e.Type == EntityType.Monster && !IsBackfill(e));
        var backfilledCount = final.Count(e => e.Type == EntityType.Monster && IsBackfill(e));

        return (final, new MonsterNameCleanupCounts(
            cleaned, deduped, groundedCollisionsFlagged, groundedCount, backfilledCount));
    }
}
