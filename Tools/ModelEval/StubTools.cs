using Microsoft.Extensions.AI;

namespace DndMcpAICsharpFun.Tools.ModelEval;

internal static class StubTools
{
    public static IList<AITool> Build() =>
    [
        AIFunctionFactory.Create(
            (int? marketValue = null, string? rarity = null, int? crafters = null) =>
            {
                StubState.InvokedTools.Add("calculate_crafting");
                var hasValue = marketValue.HasValue;
                var hasRarity = !string.IsNullOrWhiteSpace(rarity);
                if (hasValue == hasRarity)
                    return (object)new { error = "Supply exactly ONE of marketValue or rarity." };
                if (hasValue)
                    return new { kind = "nonmagical", materialsGp = 750, totalWorkweeks = 30, days = 150, citation = "Xanathar's Guide to Everything — Crafting" };
                return new { kind = "magic-item", rarity = rarity, workweeks = 10, goldCostGp = 2000, citation = "Xanathar's Guide to Everything — Crafting Magic Items" };
            },
            name: "calculate_crafting",
            description: "Calculate the EXACT time and cost to CRAFT an item. For a NONMAGICAL item pass its marketValue (gold) and optionally crafters; for a MAGIC item pass its rarity (common/uncommon/rare/very rare/legendary). Supply exactly ONE of marketValue or rarity. Report the returned numbers EXACTLY; never re-derive."),

        AIFunctionFactory.Create(
            (string concept, string archetype, double? maxCr = null) =>
            {
                StubState.InvokedTools.Add("generate_npc");
                return (object)new { name = "Garret Vosk", archetype, archetypeInCorpus = true, cr = "1", hp = 27, source = "Monster Manual", block = "Spy (CR 1): AC 12, HP 27, ..." };
            },
            name: "generate_npc",
            description: "Generate an NPC for a scene from a concept. YOU pick the stat-block archetype and pass it as archetype (e.g. Spy, Guard, Bandit Captain); the tool returns that archetype's REAL stat block. Take ALL mechanical stats from the returned block and CITE its source; never invent stat numbers."),

        AIFunctionFactory.Create(
            (string theme) =>
            {
                StubState.InvokedTools.Add("generate_npc_party");
                return (object)new
                {
                    theme,
                    members = new[]
                    {
                        new { role = "leader", name = "Mara Kell", archetype = "Bandit Captain", source = "Monster Manual" },
                        new { role = "support", name = "Dross", archetype = "Thug", source = "Monster Manual" },
                        new { role = "support", name = "Wren", archetype = "Spy", source = "Monster Manual" },
                    },
                };
            },
            name: "generate_npc_party",
            description: "Generate a themed CAST of NPCs from a single theme string (e.g. 'a Sharn heist crew'). Returns an ENSEMBLE — a leader plus supporting members — each anchored to a REAL stat block. CITE each block's source; never invent stat numbers."),

        AIFunctionFactory.Create(
            (string question, string[]? ruleTopics = null, string? edition = null) =>
            {
                StubState.InvokedTools.Add("ask_rules");
                if (StubState.AskRulesReturnsEmpty)
                    return (object)new { passages = Array.Empty<object>() };
                return new { passages = new[] { new { text = "You can grapple a prone creature; prone does not prevent being grappled.", source = "Player's Handbook", section = "Combat" } } };
            },
            name: "ask_rules",
            description: "Answer a D&D RULES question (including multi-rule interactions). Identify the DISTINCT rules and pass them as ruleTopics (e.g. [\"grappling\", \"prone condition\"]); omit for a simple single-rule question. Returns cited rule passages from the core rulebooks. Compose STRICTLY from the returned passages and CITE each; if no passages are returned, say the rules don't cover it — never invent a rule."),

        AIFunctionFactory.Create(
            (string activity, string? edition = null) =>
            {
                StubState.InvokedTools.Add("plan_downtime");
                return (object)new { passages = new[] { new { text = "Crafting a nonmagical item costs workweeks equal to its value / 50 gp.", source = "Xanathar's Guide to Everything", section = "Downtime" } } };
            },
            name: "plan_downtime",
            description: "Plan a D&D DOWNTIME activity (crafting, training, carousing, research, etc.). Pass the activity as free text. Returns cited rule passages from the downtime rulebooks (Xanathar's + DMG). Compose STRICTLY from the returned passages and CITE each; never invent times or costs."),

        AIFunctionFactory.Create(
            (string difficulty, string edition, long? campaignId = null, string? theme = null, double? maxCr = null, double? minCr = null) =>
            {
                StubState.InvokedTools.Add("build_encounter");
                return (object)new { difficulty, monsters = new[] { new { name = "Hobgoblin", count = 1, cr = "0.5", source = "Monster Manual" }, new { name = "Goblin", count = 6, cr = "0.25", source = "Monster Manual" } } };
            },
            name: "build_encounter",
            description: "Build a combat encounter for a target difficulty (Trivial/Easy/Medium/Hard/Deadly) and optional theme, for the signed-in user's party (campaignId). Builds swarms — a strong anchor plus multiples of cheaper monsters — returned grouped as {monster, count}. edition is \"2014\" or \"2024\"."),

        AIFunctionFactory.Create(
            (long campaignId, string question, string? edition = null) =>
            {
                StubState.InvokedTools.Add("ask_setting_lore");
                return (object)new { passages = new[] { new { text = "Sharn, the City of Towers, is governed by a Lord Mayor and city council.", source = "Eberron: Rising from the Last War", section = "Sharn" } } };
            },
            name: "ask_setting_lore",
            description: "Answer a lore/worldbuilding question for one of the signed-in user's own campaigns, scoped to that campaign's SETTING sources. Pass campaignId and the question. Returns cited passages from the campaign's setting books. Compose STRICTLY from the returned passages and CITE each; never invent world lore."),

        AIFunctionFactory.Create(
            (long heroSnapshotId, string? targetClass = null, bool? considerDip = null) =>
            {
                StubState.InvokedTools.Add("plan_level_up");
                return (object)new { className = "Fighter", nextLevel = 5, hpDelta = 7, features = new[] { "Extra Attack" }, source = "Player's Handbook" };
            },
            name: "plan_level_up",
            description: "Plan the next level-up for a hero snapshot the signed-in user owns (heroSnapshotId). Returns the rule-grounded delta (HP, proficiency, features, spell slots) and cited options. RECOMMEND a pick ONLY from the returned options; never invent a feat, subclass, or spell."),
    ];
}
