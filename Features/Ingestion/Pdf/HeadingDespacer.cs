using System.Text.RegularExpressions;

namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

/// <summary>
/// Normalises letter-spaced decorative-caps headings produced by PDF converters.
/// <para>
/// When D&amp;D books use decorative capitalised headings, PDF-to-text conversion
/// sometimes inserts spurious spaces between characters, yielding garbled tokens
/// such as <c>ABER R ATIONS</c>, <c>H U MANOIDS</c>, or <c>BARD C OLLEGE S</c>.
/// <see cref="Normalize"/> detects all-caps headings and repeatedly collapses
/// 1–2 letter garbage fragments (non-whitelisted, non-numeric) into their neighbours
/// until a fixpoint is reached.
/// </para>
/// <para>
/// 3-letter fragments are never treated as garbage to avoid false merges such as
/// <c>WAR DOMAIN</c> → <c>WARDOMAIN</c>. Vocabulary-dependent garble
/// (e.g. <c>CON STRUCTS</c>) is left as-is and caught downstream by the
/// <c>needsReview</c> heuristic.
/// </para>
/// <para>
/// Conservative: only all-uppercase headings are processed; mixed-case input is
/// returned unchanged. When a merge decision is ambiguous the text is left as-is.
/// </para>
/// </summary>
public static partial class HeadingDespacer
{
    // ── Whitelist ────────────────────────────────────────────────────────────
    // 1-2 letter legitimate standalone words (per design contract) plus dice tokens.
    // 3-letter fragments are never garbage (threshold is 1–2), so no 3-letter entries
    // are needed here for garbage detection.
    // D&D stat/currency abbreviations (2 letters) are explicitly whitelisted to prevent
    // spurious merges such as "XP COSTS" → "XPCOSTS" or "AC BONUS" → "ACBONUS".

    private static readonly HashSet<string> Whitelist = new(StringComparer.Ordinal)
    {
        // 1-letter
        "A", "I",
        // 2-letter common words
        "OF", "TO", "IN", "ON", "AT", "BY", "OR", "AN", "AS", "IT", "IS",
        "BE", "DO", "NO", "SO", "UP", "WE",
        // 2-letter D&D stat / currency abbreviations (must never merge)
        "XP", "HP", "AC", "DC", "CR", "GP", "SP", "CP", "EP", "PP",
        // Dice tokens
        "D4", "D6", "D8", "D10", "D12", "D20",
    };

    // ── Regex ────────────────────────────────────────────────────────────────

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Normalises <paramref name="heading"/>, collapsing letter-spaced fragments.
    /// </summary>
    /// <param name="heading">Raw heading text from the PDF converter.</param>
    /// <returns>
    /// The normalised heading, or the original string unchanged if it is not an
    /// all-uppercase heading or no garbling is detected.
    /// </returns>
    public static string Normalize(string heading)
    {
        if (string.IsNullOrEmpty(heading))
            return heading;

        // Mixed-case headings are returned unchanged (conservative).
        if (!IsAllCaps(heading))
            return heading;

        var fragments = WhitespaceRun().Split(heading.Trim());
        if (fragments.Length <= 1)
            return heading;

        // Phase 1 – repeatedly merge garbage fragments until no more changes.
        // mergedFlags tracks which slots were produced by a garbage merge (used in Phase 2).
        var mergedFlags = new bool[fragments.Length];
        bool changed;

        do
        {
            changed = false;
            for (int i = 0; i < fragments.Length; i++)
            {
                if (!IsGarbage(fragments[i]))
                    continue;

                // Same-letter heuristic: if the left neighbour ends with the
                // same letter as the garbage fragment begins, absorb all three
                // (left + garbage + right) to reconstruct a word like ABERRATIONS.
                if (i > 0 && i + 1 < fragments.Length
                    && SameLetter(fragments[i - 1], fragments[i]))
                {
                    string merged = fragments[i - 1] + fragments[i] + fragments[i + 1];
                    fragments = Splice(fragments, i - 1, 3, merged);
                    mergedFlags = SpliceBool(mergedFlags, i - 1, 3, true);
                    changed = true;
                    break; // restart pass after structural change
                }

                // Right-merge: absorb current garbage into the following fragment.
                if (i + 1 < fragments.Length)
                {
                    string merged = fragments[i] + fragments[i + 1];
                    fragments = Splice(fragments, i, 2, merged);
                    mergedFlags = SpliceBool(mergedFlags, i, 2, true);
                    changed = true;
                    break;
                }

                // Left-merge: last fragment, absorb into the preceding one.
                if (i > 0)
                {
                    string merged = fragments[i - 1] + fragments[i];
                    fragments = Splice(fragments, i - 1, 2, merged);
                    mergedFlags = SpliceBool(mergedFlags, i - 1, 2, true);
                    changed = true;
                    break;
                }
            }
        }
        while (changed);

        // Phase 2 – absorb isolated 1-letter whitelisted fragments (A, I) that
        // are immediately followed by a garbage-merged fragment.
        // Example: "A CTIONS" → "ACTIONS" (from "A CTI ONS" after Phase 1).
        for (int i = 0; i + 1 < fragments.Length; i++)
        {
            if (fragments[i].Length == 1 && Whitelist.Contains(fragments[i])
                && mergedFlags[i + 1])
            {
                string merged = fragments[i] + fragments[i + 1];
                fragments = Splice(fragments, i, 2, merged);
                mergedFlags = SpliceBool(mergedFlags, i, 2, false);
                // Run Phase 2 again from start after any merge.
                i = -1;
            }
        }

        return string.Join(" ", fragments);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> if every letter character in <paramref name="text"/>
    /// is uppercase (non-letter characters such as digits and punctuation are ignored).
    /// </summary>
    private static bool IsAllCaps(string text)
    {
        bool hasLetter = false;
        foreach (char c in text)
        {
            if (char.IsLetter(c))
            {
                if (char.IsLower(c)) return false;
                hasLetter = true;
            }
        }
        return hasLetter;
    }

    /// <summary>
    /// A fragment is "garbage" (a product of letter-spacing) when it consists
    /// entirely of letters, has 1–2 characters, and is not a known standalone word.
    /// 3-letter fragments are never garbage to avoid false merges such as
    /// "WAR DOMAIN" → "WARDOMAIN" or "RED DRAGON" → "REDDRAGON".
    /// Fragments containing digits or punctuation are never garbage.
    /// </summary>
    private static bool IsGarbage(string fragment)
        => fragment.Length is >= 1 and <= 2
           && fragment.All(char.IsLetter)
           && !Whitelist.Contains(fragment);

    /// <summary>
    /// Returns <c>true</c> when <paramref name="left"/> ends with the same letter
    /// as <paramref name="fragment"/> begins — the primary signal that the garbage
    /// fragment was split from within the left neighbour's word.
    /// </summary>
    private static bool SameLetter(string left, string fragment)
        => left.Length > 0
           && fragment.Length > 0
           && char.ToUpperInvariant(left[^1]) == char.ToUpperInvariant(fragment[0]);

    // ── Array splice helpers ─────────────────────────────────────────────────

    private static string[] Splice(string[] arr, int start, int count, string replacement)
    {
        var result = new string[arr.Length - count + 1];
        arr.AsSpan(0, start).CopyTo(result);
        result[start] = replacement;
        arr.AsSpan(start + count).CopyTo(result.AsSpan(start + 1));
        return result;
    }

    private static bool[] SpliceBool(bool[] arr, int start, int count, bool replacement)
    {
        var result = new bool[arr.Length - count + 1];
        arr.AsSpan(0, start).CopyTo(result);
        result[start] = replacement;
        arr.AsSpan(start + count).CopyTo(result.AsSpan(start + 1));
        return result;
    }
}
