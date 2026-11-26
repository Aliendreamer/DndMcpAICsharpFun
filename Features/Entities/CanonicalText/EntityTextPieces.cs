using System.Text.RegularExpressions;

using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Entities.CanonicalText;

/// <summary>A run of a text: its start offset and its characters.</summary>
public sealed record TextPiece(int Start, string Text);

/// <summary>
/// Cuts an entity's text into pieces the embedding model (512 tokens) and the reranker can read whole.
/// </summary>
public static class EntityTextPieces
{
    public const int MaxChars = 1500;
    private const int OverlapChars = 150;
    private const int BreakWindow = 300;

    public static IReadOnlyList<TextPiece> Split(string text)
    {
        if (text.Length <= MaxChars) return [new TextPiece(0, text)];
        var pieces = new List<TextPiece>();
        var start = 0;
        while (true)
        {
            var end = Math.Min(start + MaxChars, text.Length);
            if (end < text.Length) end = BreakBefore(text, end);
            pieces.Add(new TextPiece(start, text[start..end]));
            if (end == text.Length) return pieces;
            var next = text.IndexOf(' ', end - OverlapChars, OverlapChars);
            start = next < 0 ? end - OverlapChars : next + 1;
        }
    }

    private static int BreakBefore(string text, int end)
    {
        foreach (var mark in new[] { "\n", ". ", " " })
        {
            var at = text.LastIndexOf(mark, end - 1, BreakWindow, StringComparison.Ordinal);
            if (at >= 0) return at + mark.Length;
        }
        return end;
    }

    /// <summary>
    /// What is embedded for an entity: the pieces of its text (with its margin notes) and of every extra text
    /// (a monster's same-named lore). Every piece but the first is headed by the entity's name and type.
    /// </summary>
    public static IReadOnlyList<string> ForEmbedding(
        string name, string type, string canonicalText, IReadOnlyList<EntityNote>? notes, IEnumerable<string> extra)
    {
        var heading = $"{name} ({type}): ";
        var pieces = Split(EntityEmbeddingText.For(canonicalText, notes)).Select((p, i) => i == 0 ? p.Text : heading + p.Text);
        return [.. pieces, .. extra.SelectMany(Split).Select(p => heading + p.Text)];
    }

    /// <summary>The text itself when it fits, else a run of at most <paramref name="maxChars"/> that holds the piece.</summary>
    public static string Window(string text, TextPiece piece, int maxChars)
    {
        if (text.Length <= maxChars) return text;
        var start = Math.Clamp(piece.Start - (maxChars - piece.Text.Length) / 2, 0, text.Length - maxChars);
        return (start > 0 ? "…" : "") + text.Substring(start, maxChars) + (start + maxChars < text.Length ? "…" : "");
    }


    /// <summary>
    /// The piece of <paramref name="text"/> holding the most of the question's words, when it holds more than
    /// <paramref name="best"/>: a reranker prefers prose to a table ("17th +6 … 6 +4") or a late section
    /// ("Quickened Spell"), which the question's own words find.
    /// </summary>
    public static TextPiece MostAsked(string text, TextPiece best, string question)
    {
        var words = AskedWords(question);
        if (words.Count == 0) return best;
        int Hits(TextPiece p) => words.Count(w => p.Text.Contains(w, StringComparison.OrdinalIgnoreCase));
        var top = Split(text).MaxBy(Hits)!;
        return Hits(top) > Hits(best) ? top : best;
    }

    private static readonly HashSet<string> CommonWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "what", "which", "when", "where", "does", "have", "with", "many", "much", "that", "this", "they",
        "their", "them", "there", "from", "into", "about", "after", "before", "would", "could", "should",
        "will", "your", "mine", "doing", "make", "each", "every", "than", "then",
    };

    private static HashSet<string> AskedWords(string question) =>
        Regex.Split(question, @"[^\p{L}\p{N}]+")
            .Where(w => (w.Length > 3 || w.Any(char.IsDigit)) && !CommonWords.Contains(w))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}