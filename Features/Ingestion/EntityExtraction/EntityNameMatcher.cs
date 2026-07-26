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
    public (string Canonical, EntityType Type)? Match(string rawName) => Project(Scan(rawName, null));

    /// <summary>
    /// Like <see cref="Match"/>, but restricted to entries of the given <paramref name="type"/>.
    /// Lets a caller resolve a cross-type name collision in favour of a preferred type
    /// (e.g. "Dwarf" → the Dwarf <em>Race</em> entry rather than the Dwarf Monster entry).
    /// Returns null when no entry of that type matches (exact or fuzzy).
    /// </summary>
    public (string Canonical, EntityType Type)? MatchOfType(string rawName, EntityType type) => Project(Scan(rawName, type));

    /// <summary>
    /// Like <see cref="Match"/>, but PREFERRING entries whose 5etools source is
    /// <paramref name="sourceKey"/>, falling back to the global result when the book has no matching
    /// entry. Answers "what is this heading, for THIS book?" — which is what stops a heading being
    /// typed by whichever file happened to load first: `EntityNameIndex` loads `bestiary-*` before
    /// `races.json`, so the global lookup types EGW's "Orc" page as a Monster when EGW's own Orc is a
    /// Race. A null/blank <paramref name="sourceKey"/> is simply a global lookup.
    /// <para>
    /// Same confidence bar as the other two: exact normalised hit first, then bounded fuzzy at the
    /// same ±2 length window and 0.90 ratio. Source preference changes WHICH entries are considered,
    /// never how confidently a name must match.
    /// </para>
    /// </summary>
    public (string Canonical, EntityType Type)? MatchPreferSource(string rawName, string? sourceKey) =>
        Project(string.IsNullOrWhiteSpace(sourceKey)
            ? Scan(rawName, null)
            : Scan(rawName, null, sourceKey) ?? Scan(rawName, null));

    private static (string Canonical, EntityType Type)? Project((string Canonical, EntityType Type, string Source)? entry) =>
        entry is { } e ? (e.Canonical, e.Type) : null;

    // Single bounded-fuzzy Levenshtein scan over EntriesByName shared by all three public lookups.
    // Entries is always the first-wins winner per normalized key — i.e. EntriesByName[key][0] —
    // so the unfiltered path (type and requireSource both null) scans EntriesByName exclusively by
    // only ever considering each key's first entry, while a filtered path considers every entry of
    // that key matching the requested EntityType and/or source. Same distance bound (±2), same 0.90
    // ratio threshold, same ordinal tie-break on Canonical, same return semantics throughout.
    private (string Canonical, EntityType Type, string Source)? Scan(
        string rawName, EntityType? type, string? requireSource = null)
    {
        // Both stat-line strips run before normalisation: MinerU collapses either a monster's
        // "<Size> <type>" line or a magic item's "<category>, <rarity>" line into the heading, and a
        // raw concatenated name normalises to a key that exists in no index (so it would match
        // nothing and lose its grounding). See extraction-statline-name-repair.
        var normalized = EntityNameIndex.Normalize(
            MagicItemStatLineName.Strip(MonsterStatLineName.Strip(rawName)));

        var filtered = type is not null || requireSource is not null;
        if (filtered && normalized.Length == 0) return null;

        bool Accepts((string Canonical, EntityType Type, string Source) e) =>
            (type is null || e.Type == type)
            && (requireSource is null || string.Equals(e.Source, requireSource, StringComparison.OrdinalIgnoreCase));

        // 1. Exact normalized hit (optionally restricted to the requested type and/or source).
        if (index.EntriesByName.TryGetValue(normalized, out var exactList))
        {
            if (!filtered)
                return exactList[0];

            foreach (var e in exactList)
                if (Accepts(e)) return e;
        }

        // 2. Bounded fuzzy: only compare against index keys within ±2 chars of the query
        //    to keep the cost proportional to a small candidate set.
        var queryLen = normalized.Length;
        (string Canonical, EntityType Type, string Source)? best = null;
        var bestRatio = 0.0;
        string? bestCanonical = null;

        foreach (var (key, values) in index.EntriesByName)
        {
            if (Math.Abs(key.Length - queryLen) > 2) continue;

            var dist = Levenshtein(normalized, key);
            var maxLen = Math.Max(queryLen, key.Length);
            var ratio = maxLen == 0 ? 1.0 : 1.0 - (double)dist / maxLen;

            if (!filtered)
            {
                var value = values[0];
                if (ratio > bestRatio || (ratio == bestRatio && string.CompareOrdinal(value.Canonical, bestCanonical) < 0))
                {
                    bestRatio = ratio;
                    bestCanonical = value.Canonical;
                    best = value;
                }
            }
            else
            {
                foreach (var value in values)
                {
                    if (!Accepts(value)) continue;
                    if (ratio > bestRatio || (ratio == bestRatio && string.CompareOrdinal(value.Canonical, bestCanonical) < 0))
                    {
                        bestRatio = ratio;
                        bestCanonical = value.Canonical;
                        best = value;
                    }
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