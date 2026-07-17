namespace DndMcpAICsharpFun.Features.Chat.Routing;

/// <summary>
/// Static tool-name → group map for the chat query router (chat-query-router). The classifier tags
/// a query to one group; the router then offers only that group's tools plus the always-safe core.
/// A tool name NOT in <see cref="Map"/> is treated as always-offered, so a newly-added chat tool is
/// never silently hidden by an out-of-date map.
/// </summary>
public static class ToolGroups
{
    public const string RetrievalLore = "retrieval-lore";
    public const string StructuredLookup = "structured-lookup";
    public const string CharacterResolution = "character-resolution";
    public const string Calculators = "calculators";
    public const string Generation = "generation";

    public static readonly IReadOnlyDictionary<string, string> Map =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // narrative / lore / rules → prose RAG
            ["search_lore"] = RetrievalLore,
            ["search_dnd"] = RetrievalLore,
            ["ask_setting_lore"] = RetrievalLore,
            ["ask_rules"] = RetrievalLore,
            // filter / lookup over structured entities
            ["search_entities"] = StructuredLookup,
            ["get_entity"] = StructuredLookup,
            // deterministic character-resolution engine
            ["resolve_character_feature"] = CharacterResolution,
            ["check_multiclass"] = CharacterResolution,
            // deterministic calculators
            ["calculate_crafting"] = Calculators,
            // generation / advice / design
            ["generate_npc"] = Generation,
            ["generate_npc_party"] = Generation,
            ["prep_session"] = Generation,
            ["plan_downtime"] = Generation,
            ["plan_level_up"] = Generation,
            ["recommend_build"] = Generation,
            ["critique_build"] = Generation,
            ["rate_encounter"] = Generation,
            ["build_encounter"] = Generation,
        };

    /// <summary>All group keys — used to build the exemplar index and validate options.</summary>
    public static readonly IReadOnlyList<string> All =
        [RetrievalLore, StructuredLookup, CharacterResolution, Calculators, Generation];
}
