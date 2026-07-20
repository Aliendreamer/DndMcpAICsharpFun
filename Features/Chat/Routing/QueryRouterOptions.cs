namespace DndMcpAICsharpFun.Features.Chat.Routing;

/// <summary>
/// Options for the chat query router (bound from the "ChatQueryRouter" config section; appsettings
/// are git-crypt-masked, so live overrides come via <c>ChatQueryRouter__*</c> env). Code defaults
/// are the source of truth.
/// </summary>
public sealed class QueryRouterOptions
{
    /// <summary>Master toggle. When false the router is a no-op (full tool set, pre-router behavior).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Minimum classifier confidence to narrow the tool set. Below this → offer all tools.
    /// Deterministic signal hits report confidence 1.0 and always clear this bar; the threshold
    /// governs only the embedding backstop.</summary>
    public double Threshold { get; init; } = 0.45;

    /// <summary>Tools always offered, in every narrowed set, so the model is never stranded — the
    /// fused prose search can partially answer almost anything.</summary>
    public string[] AlwaysSafeToolNames { get; init; } = ["search_lore"];

    /// <summary>Per-group seed phrases; embedded once into centroids for the backstop classifier.
    /// Editable to tune routing without a code change.</summary>
    public Dictionary<string, string[]> Exemplars { get; init; } = new(StringComparer.Ordinal)
    {
        [ToolGroups.RetrievalLore] =
        [
            "what does the fireball spell do",
            "how does grappling work",
            "describe the city of Waterdeep",
            "tell me the lore of the Dragonmarked Houses",
            "explain the rules for opportunity attacks",
        ],
        [ToolGroups.StructuredLookup] =
        [
            "list all monsters of challenge rating 5",
            "which spells can a wizard learn at level 3",
            "find every race that grants a constitution bonus",
            "show me all fire cantrips",
            "how many legendary actions does a beholder have",
        ],
        [ToolGroups.CharacterResolution] =
        [
            "what is my breath weapon",
            "what do I get at level 8",
            "can I cast counterspell",
            "what is my spell save DC",
            "how many spell slots do I have",
            "what are my class features",
            "what spells does my subclass give me",
        ],
        [ToolGroups.Calculators] =
        [
            "how long does it take to craft plate armor",
            "what is the cost and time to craft a rare magic item",
            "calculate the crafting materials for a longsword",
        ],
        [ToolGroups.Generation] =
        [
            "generate an NPC innkeeper",
            "make me a villain for my campaign",
            "prep a session for tonight",
            "build me an encounter for four level-5 heroes",
            "recommend a build for a tanky paladin",
        ],
    };
}