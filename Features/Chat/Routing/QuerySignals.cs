using System.Text.RegularExpressions;

namespace DndMcpAICsharpFun.Features.Chat.Routing;

/// <summary>
/// High-precision deterministic signal pass for the query router. Returns a tool group ONLY when
/// exactly one signal family matches — an ambiguous query (0 or ≥2 matches) returns null and defers
/// to the embedding backstop. This keeps the fast path precise: it fires on clear intent and stays
/// silent when unsure, rather than guessing.
/// </summary>
public static partial class QuerySignals
{
    /// <summary>The group when exactly one signal family matches; null when none or several do.</summary>
    public static string? Detect(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;

        string? hit = null;
        var count = 0;
        void Consider(bool matched, string group)
        {
            if (!matched) return;
            count++;
            hit = group;
        }

        // Order is irrelevant — a ≥2 match returns null regardless, so precision is order-free.
        Consider(GenerationSignal().IsMatch(query), ToolGroups.Generation);
        Consider(CharacterResolutionSignal().IsMatch(query), ToolGroups.CharacterResolution);
        Consider(StructuredLookupSignal().IsMatch(query), ToolGroups.StructuredLookup);

        return count == 1 ? hit : null;
    }

    // Imperative content-creation: "generate an NPC", "make me a villain", "prep a session",
    // "build me an encounter", "recommend a build", "critique my build".
    [GeneratedRegex(
        @"\b(generate|make me an?|build me an?|prep(are)?\s+(a\s+)?session|recommend\s+(a|an)\s+build|critique\s+(my\s+)?build|design\s+(a|an)\s+(npc|encounter|monster|villain))\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex GenerationSignal();

    // Character-referential: possessive "my", or first-person modal ("can I cast", "do I have",
    // "what do I get"). These are questions about THIS character → the resolution engine.
    [GeneratedRegex(
        @"\bmy\b|\b(can|do|will|should|could)\s+i\b|\bwhat\s+do\s+i\s+get\b|\bfor\s+my\s+(character|hero|pc)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex CharacterResolutionSignal();

    // Set / quantifier phrasing that wants a returned SET, not a passage.
    [GeneratedRegex(
        @"\b(all|every|list|how\s+many)\b|\bwhich\s+\w+.*\b(can|are|have|grant)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex StructuredLookupSignal();
}
