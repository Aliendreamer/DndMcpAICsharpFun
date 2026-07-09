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