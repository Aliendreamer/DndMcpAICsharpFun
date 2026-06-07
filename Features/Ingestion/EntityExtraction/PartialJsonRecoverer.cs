using System.Text.Json;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Recovers the longest valid JSON prefix from a truncated/garbage-suffixed
/// LLM response by tracking brace/bracket depth (string-aware) and validating
/// the prefix at the last point depth returned to zero.
/// </summary>
public sealed class PartialJsonRecoverer
{
    public bool TryRecover(string raw, out string recovered)
    {
        recovered = string.Empty;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        int depth = 0;
        int lastValidClose = -1;
        bool inString = false;
        bool escaped = false;
        bool seenOpen = false;

        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];

            if (inString)
            {
                if (escaped) { escaped = false; }
                else if (c == '\\') { escaped = true; }
                else if (c == '"') { inString = false; }
                continue;
            }

            switch (c)
            {
                case '"': inString = true; break;
                case '{' or '[': depth++; seenOpen = true; break;
                case '}' or ']':
                    depth--;
                    if (depth == 0 && seenOpen) lastValidClose = i;
                    break;
            }
        }

        if (lastValidClose < 0) return false;

        var candidate = raw[..(lastValidClose + 1)].TrimStart();
        try
        {
            using var _ = JsonDocument.Parse(candidate);
            recovered = candidate;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
