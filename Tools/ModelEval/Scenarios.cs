namespace DndMcpAICsharpFun.Tools.ModelEval;

internal static class Scenarios
{
    private static bool Has(string text, string needle) =>
        text.Contains(needle, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<Scenario> All { get; } =
    [
        new("craft-nonmagical", "I want to craft a suit of plate armor with a market value of 1500 gp. How long and how much?",
            "calculate_crafting", false, t => Has(t, "750"),
            NoListCheck: t => SymptomChecks.NoList(t),
            NumberLabelCheck: t => SymptomChecks.NumberLabel(t, "750", "cost", ["market value", "market price"])),
        new("craft-magic", "How long does it take to craft a rare magic item?",
            "calculate_crafting", false, t => Has(t, "2000"),
            NoListCheck: t => SymptomChecks.NoList(t),
            NumberLabelCheck: t => SymptomChecks.NumberLabel(t, "2000", "cost", ["market value", "sale price"])),
        new("npc-party", "Give me a criminal gang to run a heist against the party.",
            "generate_npc_party", false, t => Has(t, "Mara") || Has(t, "leader") || Has(t, "member"),
            NoListCheck: t => SymptomChecks.NoList(t)),
        new("npc-single", "I need a single shifty dockworker NPC for this scene.",
            "generate_npc", false, t => Has(t, "Garret") || Has(t, "Spy"),
            NoListCheck: t => SymptomChecks.NoList(t)),
        new("rules-grapple", "Can I grapple a creature that is already prone?",
            "ask_rules", false, t => Has(t, "Player's Handbook") || Has(t, "PHB"),
            NoListCheck: t => SymptomChecks.NoList(t)),
        new("downtime-craft", "How long would it take to craft this during downtime between adventures?",
            "plan_downtime", false, t => Has(t, "Xanathar")),
        new("encounter-build", "Build a hard combat encounter for my party.",
            "build_encounter", false, t => Has(t, "Hobgoblin") || Has(t, "Goblin")),
        new("setting-lore", "Who rules the city of Sharn in my Eberron campaign?",
            "ask_setting_lore", false, t => Has(t, "Lord Mayor") || Has(t, "council")),
        new("rules-empty", "What are the exact rules for holding your breath while swimming in lava?",
            "ask_rules", true, t => Has(t, "don't cover") || Has(t, "doesn't cover") || Has(t, "not covered") ||
                Has(t, "don't directly") || Has(t, "no passages") || Has(t, "no rules") || Has(t, "isn't covered") || Has(t, "aren't covered")),
        new("no-tool", "Hi there, how's it going today?",
            null, false, _ => true),
    ];
}
