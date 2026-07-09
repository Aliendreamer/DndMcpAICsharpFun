using System.Text.RegularExpressions;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public static partial class ExtractionNeedsReview
{
    // Detects a standalone single-letter word followed by a space and another word.
    // e.g. "f eature" in "Path of the Beast f eature".
    // Anchors to start-of-string or a space to avoid matching possessives like "'s C".
    [GeneratedRegex(@"(?:^| )[a-z] [a-z]", RegexOptions.IgnoreCase)]
    private static partial Regex SplitWordPattern();

    [GeneratedRegex(@"\.{3,}")]
    private static partial Regex NoisePattern();

    public static bool Derive(string name, string? confidence) =>
        confidence is "low" or "medium" || HasOcrArtifacts(name);

    public static bool HasOcrArtifacts(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        // Fully uppercased multi-letter name
        if (name.Length > 1 && name == name.ToUpperInvariant() && name.Any(char.IsLetter))
            return true;
        // OCR-split word: a lone single-letter word before another word
        if (SplitWordPattern().IsMatch(name))
            return true;
        // Noise characters
        if (NoisePattern().IsMatch(name))
            return true;
        // Per-word case alternation check (catches "YouR", "WoRLD" etc.)
        foreach (var word in name.Split(' ', '-'))
        {
            if (CountCaseAlternations(word) >= 2) return true;
        }
        return false;
    }

    private static int CountCaseAlternations(string word)
    {
        var letters = word.Where(char.IsLetter).ToArray();
        int count = 0;
        for (int i = 1; i < letters.Length; i++)
            if (char.IsUpper(letters[i]) != char.IsUpper(letters[i - 1]))
                count++;
        return count;
    }
}