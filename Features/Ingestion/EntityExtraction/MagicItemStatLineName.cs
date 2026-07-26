using System.Text.RegularExpressions;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Magic-item stat-line handling for candidate NAMES, the sibling of <see cref="MonsterStatLineName"/>.
/// MinerU sometimes collapses an item heading and its stat line into one heading, yielding either a
/// name with a trailing stat line ("AMULET OF THE DRUNKARD Wondrous item, uncommon" — repairable) or a
/// name that is nothing but the stat line ("Wondrous item, very rare" — not an entity at all).
/// <para>
/// Both shapes are recognised through the SAME pattern so the repair and the reject cannot drift:
/// <see cref="Strip"/> removes a trailing occurrence, <see cref="IsWholeStatLine"/> reports a leading
/// one. Deliberately tighter than <c>ExtractionSignatures.MagicItemHeader()</c> (which scans item BODY
/// text): the gap between the category token and the rarity must be empty or a parenthesised sub-type,
/// so a real item name that merely ends in a rarity ("Wand of Fireballs, rare") is never mistaken for
/// a bare stat line.
/// </para>
/// </summary>
public static partial class MagicItemStatLineName
{
    private const string Shape =
        @"(?:weapon|armou?r|ring|rod|staff|wand|potion|scroll|wondrous(?:\s+item)?)\b" +
        @"(?:\s*\([^)\n]{0,40}\))?\s*,\s*" +
        @"(?:common|uncommon|very\s+rare|rare|legendary|artifact)\b";

    [GeneratedRegex(@"\s+" + Shape + ".*$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingStatLine();

    [GeneratedRegex("^" + Shape, RegexOptions.IgnoreCase)]
    private static partial Regex LeadingStatLine();

    /// <summary>
    /// Removes a trailing magic-item stat line, if present. Returns the input unchanged when there is
    /// no match, or when stripping the match would leave nothing — so a name that is ENTIRELY a stat
    /// line falls through to <see cref="IsWholeStatLine"/> rather than becoming an empty name.
    /// </summary>
    public static string Strip(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName)) return rawName;

        var match = TrailingStatLine().Match(rawName);
        if (!match.Success) return rawName;

        var stripped = rawName[..match.Index].Trim();
        return stripped.Length == 0 ? rawName : stripped;
    }

    /// <summary>
    /// Whether the name IS a magic-item stat line rather than an entity name — i.e. it begins with the
    /// stat-line shape, so there is no entity name in front of it to keep. Such a candidate is not an
    /// entity whatever its surrounding text signature says.
    /// </summary>
    public static bool IsWholeStatLine(string? name) =>
        !string.IsNullOrWhiteSpace(name) && LeadingStatLine().IsMatch(name.Trim());
}