using System.Text.Json;
using System.Text.RegularExpressions;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.Programmatic;

/// <summary>
/// A Lore entity whose whole text is one sentence that only introduces what follows ("Here are sample adventure
/// hooks…", "The spells are presented in alphabetical order.") or a back-cover blurb. It carries no content of its own,
/// so its disposition is <see cref="EntityDisposition.Declined"/> (openspec lead-in-lore-declined).
/// </summary>
public static partial class LeadInLore
{
    [GeneratedRegex(
        @"\b(the following|here are|here's what you need to know|following (section|table|subsections?)|sections that follow" +
        @"|this (section|chapter|appendix) (presents|provides|offers|contains|describes|includes)|(are|is) presented" +
        @"|presents (a|an|one|two|three|four|several|the|new)\b|tables? (below|offers|describes|provides|presents)" +
        @"|use the [\w'\- ]+ tables?\b|roll on the|the [\w'\- ]+ table (offers|describes|provides|presents|lists)" +
        @"|the world's greatest roleplaying game)",
        RegexOptions.IgnoreCase)]
    private static partial Regex IntroPhrase();

    // A colon does not end a sentence: "…one new subclass option: the Path of the Giant…" is one sentence.
    [GeneratedRegex(@"[.!?](\s|$)")]
    private static partial Regex SentenceEnd();

    // An introduction says so up front; "resistance to one of the following damage types" deep in a rule is not one.
    private const int IntroWithin = 60;

    public static bool Is(EntityEnvelope e)
    {
        if (e.Type != EntityType.Lore || e.Fields.ValueKind != JsonValueKind.Object
            || !e.Fields.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array
            || entries.GetArrayLength() != 1 || entries[0].ValueKind != JsonValueKind.String)
            return false;
        var text = entries[0].GetString()!.Trim();
        if (SentenceEnd().Count(text) > 1) return false;
        var m = IntroPhrase().Match(text);
        return m.Success && (m.Index < IntroWithin || m.Value.EndsWith("roleplaying game", StringComparison.OrdinalIgnoreCase));
    }
}
