using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Single source of truth for content/name recognition in the extraction pipeline. OCR-tolerant,
/// case-insensitive. All stat-block / magic-item / name-quality checks route through here rather
/// than re-matching marker strings independently.
/// </summary>
public static partial class ExtractionSignatures
{
    public static bool HasArmorClass(string text) => Has(text, "Armor Class");
    public static bool HasHitPoints(string text) => Has(text, "Hit Points");
    public static bool HasChallenge(string text) => Has(text, "Challenge");

    /// A complete creature stat block: Armor Class + Hit Points + Challenge.
    public static bool IsCompleteStatBlock(string text) =>
        !string.IsNullOrEmpty(text) && HasArmorClass(text) && HasHitPoints(text) && HasChallenge(text);

    /// An OBJECT stat block: a "&lt;Size&gt; object" line with Armor Class + Hit Points but NO Challenge.
    /// Objects (siege weapons, animated objects, doors) are non-creatures — no ability scores or CR —
    /// so this reliably distinguishes them from a Monster stat block.
    public static bool IsObjectStatBlock(string text) =>
        !string.IsNullOrEmpty(text) && HasArmorClass(text) && HasHitPoints(text)
        && !HasChallenge(text) && ObjectSizeType().IsMatch(text);

    /// A magic item: explicit attunement / wondrous-item phrasing, or a "<category>, <rarity>" header line.
    public static bool IsMagicItem(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (Has(text, "requires attunement") || Has(text, "wondrous item")) return true;
        return MagicItemHeader().IsMatch(text);
    }

    /// Whether a candidate name is a real entity name (true) vs a section heading / stat-block fragment
    /// (false). Conservative: rejects only clear non-entities; when unsure returns true.
    public static bool IsEntityLikeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var n = name.Trim();
        if (!n.Any(char.IsLetter)) return false;
        if (char.IsDigit(n[0])) return false;
        if (StepHeading().IsMatch(n)) return false;
        if (ChallengeFragment().IsMatch(n)) return false;
        if (SectionHeading().IsMatch(n)) return false;
        if (n.StartsWith("Appendix", StringComparison.OrdinalIgnoreCase)) return false;
        if (StructuralHeaders.Contains(n)) return false;
        if (LairHeading().IsMatch(n) && n.StartsWith("A ", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }


    /// A subclass-feature-progression signature: two or more DISTINCT level-gated feature grants
    /// ("Starting at 3rd level…", "At 6th level…", "When you reach 10th level…", "beginning at
    /// 14th level…", or the hyphenated "3rd-level" form). A single level-gated phrase can appear in
    /// ordinary prose (a spell reference, a one-off caveat); two or more DISTINCT levels is the
    /// structural marker of a subclass write-up (3rd/6th/10th/14th feature progression), so we dedupe
    /// on the matched level token before counting.
    public static bool HasSubclassFeatureProgression(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var matches = LevelGatedFeature().Matches(text);
        if (matches.Count < 2) return false;
        var distinctLevels = matches
            .Select(m => (m.Groups["lvl"].Success ? m.Groups["lvl"] : m.Groups["lvl2"]).Value.ToLowerInvariant())
            .Distinct()
            .Count();
        return distinctLevels >= 2;
    }

    /// Minimum character length for a body to count as "substantial" prose. Chapter-noise fragments
    /// ("Ability Score Increase", "CONTENTS", a bare table header) run well under 50 characters; a
    /// real background/feat prose blurb runs to several sentences (typically 300+ characters). 150 is
    /// a conservative midpoint that passes real prose and rejects fragments/headings.
    private const int MinProseBodyLength = 150;

    /// Whether a candidate body is real, substantial prose — long enough to be a genuine writeup, and
    /// NOT dominated by a flattened dice/rolling table (e.g. a "d6 Personality Trait" table whose rows
    /// survive text extraction as "1  Foo 2  Bar 3  Baz…"). The extraction pipeline flattens columnar
    /// stat-block/table text using multi-space gaps (see the AC/HP/Challenge fixtures in
    /// ExtractionSignaturesTests), so a run of "&lt;1-2 digits&gt;&lt;2+ spaces&gt;" cells alongside a
    /// die-size token ("d6"/"d8"/"d10"/"d12"/"d20"/"d100") is the same artifact for prose tables.
    /// "Dominated" is judged by span, not raw count, so a short trailing table appended after a long
    /// prose body doesn't fail a real entity.
    public static bool HasSubstantialProseBody(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.Trim();
        if (trimmed.Length < MinProseBodyLength) return false;
        if (TocDotLeader().IsMatch(trimmed)) return false;
        return !IsTableDominated(trimmed);
    }

    private static bool IsTableDominated(string text)
    {
        if (!DiceNotation().IsMatch(text)) return false;
        var cells = TableCell().Matches(text);
        if (cells.Count < 3) return false;
        var span = cells[^1].Index + cells[^1].Length - cells[0].Index;
        return span >= text.Length / 2;
    }

    [GeneratedRegex(
        @"\b(?:starting\s+at|beginning\s+at|at|when\s+you\s+reach)\s+(?<lvl>\d{1,2}(?:st|nd|rd|th)?|first|second|third|fourth|fifth|sixth|seventh|eighth|ninth|tenth|eleventh|twelfth|thirteenth|fourteenth|fifteenth|sixteenth|seventeenth|eighteenth|nineteenth|twentieth)\s+level\b" +
        @"|\b(?<lvl2>\d{1,2}(?:st|nd|rd|th))-level\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex LevelGatedFeature();

    [GeneratedRegex(@"\bd(?:4|6|8|10|12|20|100)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DiceNotation();

    // A "column cell" artifact of flattened tabular text: 1-2 digits followed by 2+ spaces, the same
    // gap convention the stat-block fixtures use ("Armor Class 17  Hit Points 135").
    [GeneratedRegex(@"\b\d{1,2}\s{2,}(?=\S)")]
    private static partial Regex TableCell();

    // A table-of-contents dot-leader line: "Chapter 3 ..... 42".
    [GeneratedRegex(@"\.{3,}\s*\d+\s*(?:\n|$)")]
    private static partial Regex TocDotLeader();

    /// A book-derived predicate that gates admission for a gated-prior candidate with no 5etools
    /// match: true when the candidate's OWN text/name prove it's a real entity — independent of any
    /// external index. A structural signature (stat block / object stat block / magic item /
    /// subclass-feature progression) suffices on its own; otherwise an entity-like name paired with a
    /// substantial, non-tabular body admits the prose-only gated types (Background, Feat, Race, God).
    public static bool IsRealEntity(EntityCandidate candidate) =>
        IsCompleteStatBlock(candidate.Text)
        || IsObjectStatBlock(candidate.Text)
        || IsMagicItem(candidate.Text)
        || HasSubclassFeatureProgression(candidate.Text)
        || (IsEntityLikeName(candidate.DisplayName) && HasSubstantialProseBody(candidate.Text));

    [GeneratedRegex(
        @"\b(weapon|armou?r|ring|rod|staff|wand|potion|scroll|wondrous)\b[^.\n]{0,40}?,\s*(common|uncommon|rare|very rare|legendary|artifact)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex MagicItemHeader();
    [GeneratedRegex(@"^step\s+\d+\b", RegexOptions.IgnoreCase)]
    private static partial Regex StepHeading();
    [GeneratedRegex(@"^challenge\s+\d", RegexOptions.IgnoreCase)]
    private static partial Regex ChallengeFragment();
    // Tutorial / section headings: "Creating a Monster" (incl. all-caps "CREATING A …"), "Monster
    // Features", "Class Features". All such corpus names are headings, never real entities.
    [GeneratedRegex(@"^creating\b|\bfeatures$", RegexOptions.IgnoreCase)]
    private static partial Regex SectionHeading();
    private static readonly FrozenSet<string> StructuralHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "ACTIONS", "REACTIONS", "TRAITS", "BONUS ACTIONS", "LEGENDARY ACTIONS", "LAIR ACTIONS", "REGIONAL EFFECTS" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    [GeneratedRegex(@"\blair\b", RegexOptions.IgnoreCase)]
    private static partial Regex LairHeading();

    [GeneratedRegex(@"\b(tiny|small|medium|large|huge|gargantuan)\s+object\b", RegexOptions.IgnoreCase)]
    private static partial Regex ObjectSizeType();

    private static bool Has(string text, string token) =>
        !string.IsNullOrEmpty(text) && text.Contains(token, StringComparison.OrdinalIgnoreCase);
}