using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Resolves a raw candidate heading to a canonical entity name + <see cref="EntityType"/> using
/// the <see cref="EntityNameIndex"/>.
/// <list type="bullet">
///   <item>First tries an exact normalized hit (covers de-spaced OCR inputs like "MAGEARMOR" → "Mage Armor").</item>
///   <item>Then fuzzy: Levenshtein ratio ≥ 0.90 against keys within ±2 length of the query.</item>
///   <item>Returns null below threshold so non-entity headings fall through to content-first logic.</item>
/// </list>
/// </summary>
public sealed class EntityNameMatcher(EntityNameIndex index)
{
    public (string Canonical, EntityType Type)? Match(string rawName)
    {
        var normalized = EntityNameIndex.Normalize(rawName);

        // 1. Exact normalized hit — handles de-spaced inputs ("magearmor" == "magearmor").
        if (index.Entries.TryGetValue(normalized, out var exact))
            return exact;

        // 2. Bounded fuzzy: only compare against index keys within ±2 chars of the query
        //    to keep the cost proportional to a small candidate set.
        var queryLen = normalized.Length;
        (string Canonical, EntityType Type)? best = null;
        var bestRatio = 0.0;
        string? bestCanonical = null;

        foreach (var (key, value) in index.Entries)
        {
            if (Math.Abs(key.Length - queryLen) > 2) continue;

            var dist = Levenshtein(normalized, key);
            var maxLen = Math.Max(queryLen, key.Length);
            var ratio = maxLen == 0 ? 1.0 : 1.0 - (double)dist / maxLen;

            if (ratio > bestRatio || (ratio == bestRatio && string.CompareOrdinal(value.Canonical, bestCanonical) < 0))
            {
                bestRatio = ratio;
                bestCanonical = value.Canonical;
                best = value;
            }
        }

        return bestRatio >= 0.90 ? best : null;
    }

    /// <summary>
    /// Like <see cref="Match"/>, but restricted to entries of the given <paramref name="type"/>.
    /// Lets a caller resolve a cross-type name collision in favour of a preferred type
    /// (e.g. "Dwarf" → the Dwarf <em>Race</em> entry rather than the Dwarf Monster entry).
    /// Returns null when no entry of that type matches (exact or fuzzy).
    /// </summary>
    public (string Canonical, EntityType Type)? MatchOfType(string rawName, EntityType type)
    {
        var normalized = EntityNameIndex.Normalize(rawName);
        if (normalized.Length == 0) return null;

        // 1. Exact normalized hit of the requested type.
        if (index.EntriesByName.TryGetValue(normalized, out var exactList))
        {
            foreach (var e in exactList)
                if (e.Type == type) return e;
        }

        // 2. Bounded fuzzy, restricted to entries of the requested type.
        var queryLen = normalized.Length;
        (string Canonical, EntityType Type)? best = null;
        var bestRatio = 0.0;
        string? bestCanonical = null;

        foreach (var (key, values) in index.EntriesByName)
        {
            if (Math.Abs(key.Length - queryLen) > 2) continue;

            var dist = Levenshtein(normalized, key);
            var maxLen = Math.Max(queryLen, key.Length);
            var ratio = maxLen == 0 ? 1.0 : 1.0 - (double)dist / maxLen;

            foreach (var value in values)
            {
                if (value.Type != type) continue;
                if (ratio > bestRatio || (ratio == bestRatio && string.CompareOrdinal(value.Canonical, bestCanonical) < 0))
                {
                    bestRatio = ratio;
                    bestCanonical = value.Canonical;
                    best = value;
                }
            }
        }

        return bestRatio >= 0.90 ? best : null;
    }

    // Standard two-row dynamic-programming Levenshtein.
    private static int Levenshtein(string s, string t)
    {
        var m = s.Length;
        var n = t.Length;

        if (m == 0) return n;
        if (n == 0) return m;

        var prev = new int[n + 1];
        var curr = new int[n + 1];

        for (var j = 0; j <= n; j++) prev[j] = j;

        for (var i = 1; i <= m; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= n; j++)
            {
                var cost = s[i - 1] == t[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(prev[j] + 1, curr[j - 1] + 1), prev[j - 1] + cost);
            }
            (prev, curr) = (curr, prev);
        }

        return prev[n];
    }
}
