using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Entities.CanonicalText;

/// <summary>A run of a text: its start offset and its characters.</summary>
public sealed record TextPiece(int Start, string Text);

/// <summary>
/// Cuts an entity's text into pieces the embedding model (512 tokens) and the reranker can read whole.
/// </summary>
public static partial class EntityTextPieces
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


    /// <summary>One line per piece of <paramref name="text"/>, numbered from 1: the headings that start in it,
    /// else its opening words. Lets the model choose the part it needs.</summary>
    public static IReadOnlyList<string> Outline(string text)
    {
        // Matched over the whole text, so a piece that starts mid-sentence does not look like a line start.
        var names = HeadingMatches(text).Select(m => (m.Groups["h"].Index, Name: Tidy(m.Groups["h"].Value))).ToList();
        return [.. Split(text).Select((p, i) =>
        {
            var heads = names.Where(n => n.Index >= p.Start && n.Index < p.Start + p.Text.Length)
                .Select(n => n.Name).Distinct().Take(MaxHeadings).ToList();
            var label = heads.Count > 0 ? string.Join(", ", heads) : Opening(p.Text);
            return $"{i + 1}: {label}";
        })];
    }

    /// <summary>Piece <paramref name="number"/> (from 1) of <paramref name="text"/>, or null when there is none.</summary>
    public static string? Part(string text, int number)
    {
        var pieces = Split(text);
        return number >= 1 && number <= pieces.Count ? pieces[number - 1].Text : null;
    }

    private const int MaxHeadings = 8;

    // What names a section in a stored text: a short capitalised line ("METAMAGIC"), a feature that opens a line
    // ("Font of Magic. You tap…"), or a title-cased option inside a paragraph ("…noted. Distant Spell When you…").
    [System.Text.RegularExpressions.GeneratedRegex(
        @"(?m)^(?<h>[A-Z][A-Za-z'’ -]{2,40})$" +
        @"|^(?<h>[A-Z][A-Za-z'’-]*(?: [A-Za-z'’-]+){0,4})\. " +
        @"|(?<=[.:] )(?<h>[A-Z][A-Za-z'’-]+(?: [A-Z][A-Za-z'’-]+){0,3}) (?=(?:When|You|If|As|While|Whenever) )")]
    private static partial System.Text.RegularExpressions.Regex SectionName();

    private static IEnumerable<System.Text.RegularExpressions.Match> HeadingMatches(string s) =>
        SectionName().Matches(s).Where(m => m.Groups["h"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 5);

    // OCR mixes case inside words ("TwInned SPEll"): title-case the heading.
    private static string Tidy(string heading) =>
        System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(heading.Trim().ToLowerInvariant());

    private static string Opening(string text)
    {
        var words = text.Split((char[]?)null, 7, StringSplitOptions.RemoveEmptyEntries).Take(6);
        return string.Join(' ', words) + " …";
    }


    /// <summary>
    /// What to show of an entity's text: the pieces whose stored vectors match the question best (the search's own
    /// comparison, piece by piece), best first, then the opening, then the next best, while they fit in
    /// <paramref name="maxChars"/>; shown in book order, a gap marked "…". Without piece vectors, a window around
    /// <paramref name="fallback"/>.
    /// </summary>
    public static string Matching(
        string text, IReadOnlyList<EntityNote>? notes, IReadOnlyList<float[]>? pieceVectors, float[] question,
        int maxChars, TextPiece fallback)
    {
        if (text.Length <= maxChars) return text;
        // The stored vectors are the pieces of the text with its notes; pieces past the text are notes or lore.
        var pieces = Split(EntityEmbeddingText.For(text, notes))
            .TakeWhile(p => p.Start < text.Length)
            .Select(p => (Start: p.Start, End: Math.Min(p.Start + p.Text.Length, text.Length)))
            .ToList();
        if (pieceVectors is null || pieceVectors.Count < pieces.Count || pieces.Count < 2)
            return Window(text, fallback, maxChars);

        var byScore = Enumerable.Range(0, pieces.Count).OrderByDescending(i => Cosine(question, pieceVectors[i])).ToList();
        byScore.Remove(0);
        byScore.Insert(Math.Min(1, byScore.Count), 0); // the opening (name, stats, class table) comes second
        var chosen = new List<(int Start, int End)>();
        foreach (var i in byScore)
        {
            var merged = Merge([.. chosen, pieces[i]]);
            if (merged.Sum(r => r.End - r.Start) > maxChars) continue;
            chosen = merged;
        }
        if (chosen.Count == 0) return Window(text, fallback, maxChars);
        return (chosen[0].Start > 0 ? "…" : "")
            + string.Join("\n…\n", chosen.Select(r => text[r.Start..r.End]))
            + (chosen[^1].End < text.Length ? "…" : "");
    }

    private static List<(int Start, int End)> Merge(IEnumerable<(int Start, int End)> ranges)
    {
        var merged = new List<(int Start, int End)>();
        foreach (var r in ranges.OrderBy(r => r.Start))
        {
            if (merged.Count > 0 && r.Start <= merged[^1].End)
                merged[^1] = (merged[^1].Start, Math.Max(merged[^1].End, r.End));
            else
                merged.Add(r);
        }
        return merged;
    }

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        return na == 0 || nb == 0 ? 0 : dot / Math.Sqrt(na * nb);
    }

}