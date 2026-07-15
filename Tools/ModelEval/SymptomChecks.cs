using System.Text.RegularExpressions;

namespace DndMcpAICsharpFun.Tools.ModelEval;

/// <summary>
/// Pure, unit-tested text checks for two persona symptoms observed in model-eval scorecards:
/// list-iness (the model answering in a numbered/bulleted list instead of prose) and number
/// mis-labeling (the model attaching a returned number to the wrong label, e.g. calling a "cost"
/// figure a "market value"). No I/O; safe to call from both ScenarioRunner and unit tests.
/// </summary>
internal static class SymptomChecks
{
    private static readonly Regex ListMarker = new(@"^(\d+[.)]\s+|[-*•]\s+)", RegexOptions.Compiled);

    /// <summary>
    /// True = adhered (prose, not a list). Splits <paramref name="text"/> into lines and counts lines
    /// whose trimmed start matches a numbered marker ("1. ", "2) ") or a bullet ("- ", "* ", "• ").
    /// Fails (returns false) once that count reaches <paramref name="threshold"/> (default 2 — 0 or 1
    /// stray marker line is tolerated, 2+ is scored as list-iness). Empty/whitespace text adheres.
    /// </summary>
    public static bool NoList(string text, int threshold = 2)
    {
        if (string.IsNullOrWhiteSpace(text))
            return true;

        var markerLines = text
            .Split('\n')
            .Count(line => ListMarker.IsMatch(line.Trim()));

        return markerLines < threshold;
    }

    /// <summary>
    /// True = adhered (value not mislabeled). Scans <paramref name="text"/> for whole-number occurrences
    /// of <paramref name="value"/> (case-insensitive, not preceded/followed by a digit — so "750" does not
    /// match inside "7500" or "1750"). For each occurrence, inspects the <paramref name="window"/>-char
    /// substring on each side (clamped to the text bounds): if that window contains any of
    /// <paramref name="wrongLabels"/> (OrdinalIgnoreCase) and does NOT contain
    /// <paramref name="correctLabel"/> (OrdinalIgnoreCase), the value is mislabeled and this returns false.
    /// If no occurrence triggers that — including when the value never appears — this returns true.
    /// </summary>
    public static bool NumberLabel(
        string text, string value, string correctLabel, IReadOnlyList<string> wrongLabels, int window = 30)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value))
            return true;

        var index = 0;
        while (true)
        {
            var found = text.IndexOf(value, index, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
                return true;

            var before = found - 1;
            var after = found + value.Length;
            var isWholeNumber =
                (before < 0 || !char.IsDigit(text[before])) &&
                (after >= text.Length || !char.IsDigit(text[after]));

            if (isWholeNumber)
            {
                var windowStart = Math.Max(0, found - window);
                var windowEnd = Math.Min(text.Length, after + window);
                var slice = text[windowStart..windowEnd];

                var hasWrongLabel = wrongLabels.Any(w => slice.Contains(w, StringComparison.OrdinalIgnoreCase));
                var hasCorrectLabel = slice.Contains(correctLabel, StringComparison.OrdinalIgnoreCase);

                if (hasWrongLabel && !hasCorrectLabel)
                    return false;
            }

            index = found + 1;
        }
    }
}
