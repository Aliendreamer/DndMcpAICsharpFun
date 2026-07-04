using System.Text.RegularExpressions;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Strips a trailing monster stat-line suffix (e.g. " Gargantuan dragon, chaotic evil") from a raw
/// candidate heading, so the remaining name resolves cleanly via <see cref="EntityNameMatcher"/>.
/// OCR/Marker conversion sometimes merges a monster's stat-line onto its heading (name + size + type
/// + alignment all on one line); this strips that suffix without touching a leading size word that is
/// legitimately part of the monster's own name (e.g. "GIANT APE").
/// </summary>
public static partial class MonsterStatLineName
{
    [GeneratedRegex(
        @"\s+(Tiny|Small|Medium|Large|Huge|Gargantuan)\s+(aberration|beast|celestial|construct|dragon|elemental|fey|fiend|giant|humanoid|monstrosity|ooze|plant|undead|swarm)\b.*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex StatLineSuffix();

    /// <summary>
    /// Removes a trailing "&lt;Size&gt; &lt;type&gt;..." stat-line suffix, if present. Returns the
    /// input unchanged when there is no match, or when stripping the match would leave nothing
    /// (i.e. the whole input was the stat line).
    /// </summary>
    public static string Strip(string rawName)
    {
        var match = StatLineSuffix().Match(rawName);
        if (!match.Success) return rawName;

        var stripped = rawName[..match.Index].Trim();
        return stripped.Length == 0 ? rawName : stripped;
    }
}
