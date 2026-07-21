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
        // extraction-noise-name-gate: a stat-block field-label line ("Armor Class …", "Damage
        // Immunities …", "Hit Points …") or an optional-rule sidebar ("Effects of …", "Variant: …")
        // mis-picked as a candidate header is not an entity, whatever its surrounding text signature.
        if (StatBlockFieldLabel().IsMatch(n)) return false;
        if (SidebarHeading().IsMatch(n)) return false;
        if (n.StartsWith("Appendix", StringComparison.OrdinalIgnoreCase)) return false;
        if (StructuralHeaders.Contains(n)) return false;
        // Any "<X> LAIR" heading (was limited to the "A "-prefixed form, so "AN ANARCH's LAIR" leaked).
        if (LairHeading().IsMatch(n)) return false;
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

    [GeneratedRegex(
        @"\b(?:starting\s+at|beginning\s+at|at|when\s+you\s+reach)\s+(?<lvl>\d{1,2}(?:st|nd|rd|th)?|first|second|third|fourth|fifth|sixth|seventh|eighth|ninth|tenth|eleventh|twelfth|thirteenth|fourteenth|fifteenth|sixteenth|seventeenth|eighteenth|nineteenth|twentieth)\s+level\b" +
        @"|\b(?<lvl2>\d{1,2}(?:st|nd|rd|th))-level\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex LevelGatedFeature();

    /// A book-derived predicate that gates admission for a gated-prior candidate with no 5etools
    /// match: true when the candidate's OWN text proves it's a real entity via a structural signature —
    /// independent of any external index. Prose-only content (no stat block / magic item / subclass
    /// progression), however entity-like the name, is NOT enough: prose-only gated types (Background,
    /// Feat, Race, God) are admitted via a 5etools match elsewhere, not via this gate.
    public static bool IsRealEntity(EntityCandidate candidate) =>
        IsCompleteStatBlock(candidate.Text)
        || IsObjectStatBlock(candidate.Text)
        || IsMagicItem(candidate.Text)
        || HasSubclassFeatureProgression(candidate.Text);

    /// <summary>
    /// A decline-bound candidate that is worth rescuing as a Rule: it has substantial prose.
    /// Name-shape / fragment / TOC filtering already happened upstream (EntityCandidateBuilder's
    /// Drop-filter via IsEntityLikeName), so by the time a candidate reaches the orchestrator's
    /// decline branch it is not a bare heading — this only adds a prose-substance floor so a thin
    /// declined stub does not become a Rule. Start permissive; the LLM's Rule-vs-none pick is the
    /// real gate (extraction-content-classification, Phase 1).
    /// </summary>
    public static bool RuleSignature(EntityCandidate candidate) =>
        (candidate.Text?.Trim().Length ?? 0) >= 200;

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
    // A "<X> LAIR" heading (ends with the word "lair"): "A RED DRAGON'S LAIR", "AN ANARCH's LAIR".
    // End-anchored so a real entity that merely contains "lair" mid-name is not rejected.
    [GeneratedRegex(@"\blair\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex LairHeading();

    // Stat-block field-label lines mis-picked as candidate names (extraction-noise-name-gate).
    // Anchored on a LEADING field label, not a substring, so "Constrictor Snake" (Con…) is untouched.
    [GeneratedRegex(
        @"^(armou?r\s+class|hit\s+points|speed|saving\s+throws|skills|senses|languages|damage\s+(immunities|resistances|vulnerabilities)|condition\s+immunities)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex StatBlockFieldLabel();

    // Optional-rule sidebar headings mis-picked as candidate names (extraction-noise-name-gate).
    [GeneratedRegex(@"^(effects\s+of\b|variant\b)", RegexOptions.IgnoreCase)]
    private static partial Regex SidebarHeading();

    [GeneratedRegex(@"\b(tiny|small|medium|large|huge|gargantuan)\s+object\b", RegexOptions.IgnoreCase)]
    private static partial Regex ObjectSizeType();

    private static bool Has(string text, string token) =>
        !string.IsNullOrEmpty(text) && text.Contains(token, StringComparison.OrdinalIgnoreCase);
}
