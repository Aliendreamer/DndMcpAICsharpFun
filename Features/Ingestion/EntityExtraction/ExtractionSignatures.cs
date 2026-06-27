using System.Text.RegularExpressions;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Single source of truth for content/name recognition in the extraction pipeline. OCR-tolerant,
/// case-insensitive. All stat-block / magic-item / name-quality checks route through here rather
/// than re-matching marker strings independently.
/// </summary>
public static class ExtractionSignatures
{
    public static bool HasArmorClass(string text) => Has(text, "Armor Class");
    public static bool HasHitPoints(string text) => Has(text, "Hit Points");
    public static bool HasChallenge(string text) => Has(text, "Challenge");

    /// A complete creature stat block: Armor Class + Hit Points + Challenge.
    public static bool IsCompleteStatBlock(string text) =>
        !string.IsNullOrEmpty(text) && HasArmorClass(text) && HasHitPoints(text) && HasChallenge(text);

    /// A magic item: explicit attunement / wondrous-item phrasing, or a "<category>, <rarity>" header line.
    public static bool IsMagicItem(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (Has(text, "requires attunement") || Has(text, "wondrous item")) return true;
        return MagicItemHeader.IsMatch(text);
    }

    /// Whether a candidate name is a real entity name (true) vs a section heading / stat-block fragment
    /// (false). Conservative: rejects only clear non-entities; when unsure returns true.
    public static bool IsEntityLikeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var n = name.Trim();
        if (!n.Any(char.IsLetter)) return false;
        if (char.IsDigit(n[0])) return false;
        if (StepHeading.IsMatch(n)) return false;
        if (ChallengeFragment.IsMatch(n)) return false;
        if (SectionHeading.IsMatch(n)) return false;
        if (n.StartsWith("Appendix", StringComparison.OrdinalIgnoreCase)) return false;
        if (n.Length >= 4 && n == n.ToUpperInvariant() && !n.Contains(' ')) return false;
        return true;
    }

    private static readonly Regex MagicItemHeader = new(
        @"\b(weapon|armou?r|ring|rod|staff|wand|potion|scroll|wondrous)\b[^.\n]{0,40}?,\s*(common|uncommon|rare|very rare|legendary|artifact)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex StepHeading = new(@"^step\s+\d+\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ChallengeFragment = new(@"^challenge\s+\d", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // Tutorial / section headings: "Creating a Monster" (incl. all-caps "CREATING A …"), "Monster
    // Features", "Class Features". All such corpus names are headings, never real entities.
    private static readonly Regex SectionHeading = new(@"^creating\b|\bfeatures$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static bool Has(string text, string token) =>
        !string.IsNullOrEmpty(text) && text.Contains(token, StringComparison.OrdinalIgnoreCase);
}
