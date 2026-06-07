using System.Text.RegularExpressions;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Splits candidate text into chunks at structural boundaries
/// (sub-headings > tables > paragraph breaks > single lines) so each chunk
/// stays under a token budget. Stateless; chars/4 approximates tokens.
/// </summary>
public sealed partial class SemanticChunker
{
    [GeneratedRegex(@"^\|[-| ]+\|\s*$")]
    private static partial Regex TableSeparator();

    public IList<string> Split(string text, int maxTokensPerChunk)
    {
        if (EstimateTokens(text) <= maxTokensPerChunk)
            return [text];

        var lines = text.Split('\n');
        var blocks = SplitIntoBlocks(lines, maxTokensPerChunk);

        var chunks = new List<string>();
        var current = new List<string>();
        var currentTokens = 0;

        foreach (var block in blocks)
        {
            var blockTokens = EstimateTokens(block);
            if (current.Count > 0 && currentTokens + blockTokens > maxTokensPerChunk)
            {
                EmitChunk(chunks, current);
                current = [];
                currentTokens = 0;
            }
            current.Add(block);
            currentTokens += blockTokens;
        }
        EmitChunk(chunks, current);
        return chunks;
    }

    private static int EstimateTokens(string s) => s.Length / 4;

    private static List<string> SplitIntoBlocks(string[] lines, int maxTokensPerChunk)
    {
        var blocks = new List<string>();
        var current = new List<string>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            bool isHeading = line.StartsWith("## ", StringComparison.Ordinal)
                          || line.StartsWith("### ", StringComparison.Ordinal);
            bool nextIsTableSeparator = i + 1 < lines.Length && TableSeparator().IsMatch(lines[i + 1]);
            bool isBlank = string.IsNullOrWhiteSpace(line);

            if ((isHeading || nextIsTableSeparator) && current.Count > 0)
            {
                blocks.Add(string.Join('\n', current));
                current = [];
            }

            current.Add(line);

            if (isBlank && current.Count > 0)
            {
                blocks.Add(string.Join('\n', current));
                current = [];
            }
        }

        if (current.Count > 0)
            blocks.Add(string.Join('\n', current));

        // A block with no internal boundaries can still exceed the budget;
        // fall back to line granularity so the greedy accumulator can work.
        var sized = new List<string>();
        foreach (var b in blocks)
        {
            if (EstimateTokens(b) > maxTokensPerChunk)
                sized.AddRange(b.Split('\n'));
            else
                sized.Add(b);
        }
        return sized;
    }

    private static void EmitChunk(List<string> chunks, List<string> blocks)
    {
        if (blocks.Count == 0) return;
        var chunk = string.Join('\n', blocks);
        if (!string.IsNullOrWhiteSpace(chunk))
            chunks.Add(chunk);
    }
}
