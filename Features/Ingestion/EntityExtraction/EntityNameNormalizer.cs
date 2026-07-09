using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Deterministic D&amp;D entity-name casing. Pure casing transform (<see cref="TitleCase"/>) plus a
/// gate (<see cref="TryNormalizeHeading"/>) that title-cases only all-caps names with no other OCR
/// artifact, so genuinely garbled names are left intact for the artifact heuristic to catch.
/// </summary>
public static partial class EntityNameNormalizer
{
    private static readonly FrozenSet<string> SmallWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "and", "but", "or", "for", "nor",
        "on", "at", "to", "in", "of", "as",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, string> Acronyms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["NPC"] = "NPC",
        ["NPCs"] = "NPCs",
        ["PC"] = "PC",
        ["PCs"] = "PCs",
        ["DM"] = "DM",
        ["GP"] = "GP",
        ["SP"] = "SP",
        ["CP"] = "CP",
        ["PP"] = "PP",
        ["EP"] = "EP",
        ["XP"] = "XP",
        ["HP"] = "HP",
        ["AC"] = "AC",
        ["DC"] = "DC",
        ["CR"] = "CR",
        ["AoE"] = "AoE",
        ["D&D"] = "D&D",
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"'[A-Z]")]
    private static partial Regex ApostropheUpperS();
    [GeneratedRegex(@"^([^A-Za-z0-9]*)(.*)")]
    private static partial Regex LeadingPunct();
    [GeneratedRegex(@"(.*?)([^A-Za-z0-9]*)$")]
    private static partial Regex TrailingPunct();

    public static string TitleCase(string name)
    {
        var parts = name.Split(' ');
        var result = new string[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Contains('-'))
            {
                var sub = part.Split('-');
                for (int j = 0; j < sub.Length; j++)
                    sub[j] = ConvertWord(sub[j], i == 0 && j == 0);
                result[i] = string.Join('-', sub);
            }
            else
            {
                result[i] = ConvertWord(part, i == 0);
            }
        }
        return string.Join(' ', result);
    }

    public static bool TryNormalizeHeading(string name, out string normalized)
    {
        bool isAllCaps = name.Length > 1 && name == name.ToUpperInvariant() && name.Any(char.IsLetter);
        bool hasOtherArtifacts = ExtractionNeedsReview.HasOcrArtifacts(name.ToLowerInvariant());
        if (isAllCaps && !hasOtherArtifacts)
        {
            normalized = TitleCase(name);
            return true;
        }
        normalized = name;
        return false;
    }

    private static string ConvertWord(string word, bool isFirst)
    {
        if (string.IsNullOrEmpty(word)) return word;

        // Strip leading/trailing punctuation so acronym and small-word lookups work on the bare token.
        var leadMatch = LeadingPunct().Match(word);
        var prefix = leadMatch.Groups[1].Value;
        var inner = leadMatch.Groups[2].Value;
        var trailMatch = TrailingPunct().Match(inner);
        var core = trailMatch.Groups[1].Value;
        var suffix = trailMatch.Groups[2].Value;

        if (string.IsNullOrEmpty(core)) return word;

        if (Acronyms.TryGetValue(core, out var canonical))
            return prefix + canonical + suffix;

        var low = core.ToLowerInvariant();
        if (!isFirst && SmallWords.Contains(low))
            return prefix + low + suffix;

        var cap = char.ToUpperInvariant(low[0]) + low[1..];
        var converted = ApostropheUpperS().Replace(cap, m => m.Value.ToLowerInvariant());
        return prefix + converted + suffix;
    }
}