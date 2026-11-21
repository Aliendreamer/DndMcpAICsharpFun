using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.Programmatic;

/// <summary>Overrides after migration, the (old, new) id moves made, and the ids left orphaned.</summary>
public sealed record MigrationResult(
    EntityOverrides Overrides,
    IReadOnlyList<(string From, string To)> Moved,
    IReadOnlyList<string> Orphaned);

/// <summary>
/// Carries overrides/approvals whose entity vanished after a re-extraction to the entity it was renamed to
/// (e.g. <c>caasting-time</c> to <c>casting-time</c>). Never guesses: a move needs a clear, unique best match.
/// </summary>
public static class OverrideMigration
{
    private const double MinSimilarity = 0.8;
    private const double MinMargin = 0.1;

    public static MigrationResult Migrate(
        EntityOverrides o, IReadOnlyList<EntityEnvelope> previous, IReadOnlyList<EntityEnvelope> extracted)
    {
        var extractedIds = extracted.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        var previousById = previous.GroupBy(e => e.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var orphanIds = o.Overrides.Select(x => x.EntityId).Concat(o.Approvals.Select(x => x.EntityId))
            .Where(id => !extractedIds.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var picks = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in orphanIds)
        {
            if (previousById.TryGetValue(id, out var prev) && PickTarget(prev, previousById, extracted) is { } target)
                picks[id] = target;
        }

        // Two orphans claiming the same target is ambiguous: neither moves.
        var contested = picks.GroupBy(p => p.Value, StringComparer.Ordinal).Where(g => g.Count() > 1)
            .Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        var moves = picks.Where(p => !contested.Contains(p.Value)).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

        string Map(string id) => moves.TryGetValue(id, out var to) ? to : id;
        var migrated = new EntityOverrides(
            o.Overrides.Select(x => x with { EntityId = Map(x.EntityId) }).ToList(),
            o.Approvals.Select(x => x with { EntityId = Map(x.EntityId) }).ToList());

        var moved = orphanIds.Where(moves.ContainsKey).Select(id => (id, moves[id])).ToList();
        var orphaned = orphanIds.Where(id => !moves.ContainsKey(id)).ToList();
        return new MigrationResult(migrated, moved, orphaned);
    }

    /// <summary>1 minus the Levenshtein distance over the longer length; 1.0 for two empty names.</summary>
    public static double NameSimilarity(string a, string b)
    {
        var max = Math.Max(a.Length, b.Length);
        return max == 0 ? 1.0 : 1.0 - (double)Levenshtein(a, b) / max;
    }

    private static string? PickTarget(
        EntityEnvelope prev, IReadOnlyDictionary<string, EntityEnvelope> previousById, IReadOnlyList<EntityEnvelope> extracted)
    {
        if (prev.Page is null)
            return null;
        var prevSlug = Slug(prev.Id);
        var scored = extracted
            .Where(c => c.Type == prev.Type && !previousById.ContainsKey(c.Id) && OnPage(c, prev.Page.Value))
            .Select(c => (c.Id, Score: NameSimilarity(prevSlug, Slug(c.Id))))
            .OrderByDescending(s => s.Score)
            .ToList();
        if (scored.Count == 0 || scored[0].Score < MinSimilarity)
            return null;
        if (scored.Count > 1 && scored[1].Score >= scored[0].Score - MinMargin)
            return null;
        return scored[0].Id;
    }

    private static bool OnPage(EntityEnvelope e, int page) =>
        e.Page == page || (e.Provenance?.Pages.Any(p => p.Page == page) ?? false);

    private static string Slug(string id) => id[(id.LastIndexOf('.') + 1)..];

    private static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }
}
