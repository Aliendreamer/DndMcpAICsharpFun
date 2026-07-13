using System.Security.Claims;

using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.CharacterAdvice;
using DndMcpAICsharpFun.Features.Encounters;
using DndMcpAICsharpFun.Features.Resolution;

using Microsoft.Extensions.AI;

namespace DndMcpAICsharpFun.Features.Chat;

public sealed class DndChatService(
    IChatClient chatClient,
    IMcpToolsProvider toolsProvider,
    ChatRepository chatRepository,
    IHttpContextAccessor httpContextAccessor,
    ChatRateLimiter rateLimiter,
    PersonaProvider personaProvider,
    CharacterResolutionService resolutionService,
    EncounterDesignService encounterService,
    LevelUpAdviceService levelUpService,
    BuildRecommenderService buildRecommender,
    BuildCritiqueService critiqueService,
    DndMcpAICsharpFun.Features.Lore.SettingLoreService settingLoreService,
    DndMcpAICsharpFun.Features.Rules.RulesAdjudicationService rulesAdjudicationService,
    DndMcpAICsharpFun.Features.Npc.NpcGenerationService npcGenerationService,
    DndMcpAICsharpFun.Features.SessionPrep.SessionPrepService sessionPrepService)
{
    public List<ChatMessage> History { get; } = [];

    /// <summary>
    /// Populate <see cref="History"/> from the signed-in user's persisted chat turns,
    /// so the conversation is replayed when the page is (re)opened. No-op if already loaded
    /// or if there is no authenticated user. Caps replay to the most recent turns to bound
    /// the LLM context window.
    /// </summary>
    public async Task LoadHistoryAsync(int maxTurns = 40)
    {
        if (History.Count > 0) return;
        var idClaim = httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!long.TryParse(idClaim, out var userId)) return;

        var turns = await chatRepository.GetHistoryAsync(userId);
        foreach (var t in turns.TakeLast(maxTurns))
            History.Add(new ChatMessage(
                t.Role == "user" ? ChatRole.User : ChatRole.Assistant, t.Content));
    }

    public async Task<bool> SendAsync(string userMessage, bool allowWebSearch, CancellationToken ct)
    {
        var ip = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!rateLimiter.TryAcquire(ip))
        {
            const string limited = "You're sending messages too quickly. Please wait a moment.";
            History.Add(new ChatMessage(ChatRole.User, userMessage));
            History.Add(new ChatMessage(ChatRole.Assistant, limited));
            return true;
        }

        var tools = await toolsProvider.GetToolsAsync(ct);
        var activeTools = allowWebSearch
            ? tools
            : tools.Where(t => t is not AIFunction fn || fn.Name != "search_web").ToList();

        // Character-scoped resolution and per-user encounter design are NOT exposed on the
        // shared-key MCP surface (SEC-08). They are added here as per-user in-process tools that
        // close over the signed-in user's id, so the ownership check in *ForUserAsync is always
        // applied. Unauthenticated callers get no tool.
        var toolList = activeTools.ToList();
        var idClaim = httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (long.TryParse(idClaim, out var userId))
        {
            toolList.Add(AIFunctionFactory.Create(
                (long heroSnapshotId, string feature, CancellationToken toolCt) =>
                    resolutionService.ResolveForUserAsync(heroSnapshotId, userId, feature, toolCt),
                name: "resolve_character_feature",
                description: "Compute a character-specific, cited rule fact for a hero snapshot the " +
                    "signed-in user owns. Supported features: \"breath weapon\", \"spell slots\" " +
                    "(multiclass-aware combined caster level; Warlock pact reported separately), " +
                    "\"spell save dc\" (one value per caster class), \"spell attack\" (spell attack " +
                    "bonus per caster class). Returns the value plus the rule components and their " +
                    "source provenance."));

            toolList.Add(AIFunctionFactory.Create(
                (long heroSnapshotId, string targetClass, CancellationToken toolCt) =>
                    resolutionService.ResolveMulticlassValidityForUserAsync(
                        heroSnapshotId, userId, targetClass, toolCt),
                name: "check_multiclass",
                description: "Check whether the signed-in user's hero snapshot may multiclass into a " +
                    "given class (targetClass, e.g. \"Rogue\"). Returns allowed/not-allowed with the " +
                    "failed ability-score prerequisite and the reduced proficiency subset the class " +
                    "grants. Works for any combination, caster or not."));

            toolList.Add(AIFunctionFactory.Create(
                (long? campaignId, int[]? partyLevels, MonsterQuantity[] monsters, string edition, CancellationToken toolCt) =>
                    encounterService.RateForUserAsync(
                        userId, campaignId, partyLevels, monsters, ParseEdition(edition), toolCt),
                name: "rate_encounter",
                description: "Rate a combat encounter's difficulty (Trivial/Easy/Medium/Hard/Deadly) " +
                    "for the signed-in user's party. The party comes from the caller's own campaign " +
                    "(campaignId) or an explicit partyLevels list (one level per party member); " +
                    "campaignId wins ownership-checked access, partyLevels overrides it when supplied. " +
                    "monsters is a list of {name, quantity} pairs — name is an entity id or free-text " +
                    "name to look up, quantity is how many of it (e.g. eight goblins is one pair " +
                    "{name:\"goblin\", quantity:8}). edition is \"2014\" or \"2024\"."));

            toolList.Add(AIFunctionFactory.Create(
                async (long? campaignId, string difficulty, string edition, string? theme,
                    double? maxCr, double? minCr, CancellationToken toolCt) =>
                {
                    var built = await encounterService.BuildForUserAsync(
                        userId, campaignId, partyLevels: null, ParseDifficulty(difficulty),
                        ParseEdition(edition), theme, crLte: maxCr, crGte: minCr, toolCt);
                    var a = built.Assessment;
                    return new BuiltEncounterView(
                        a.Difficulty, a.TotalMonsterXp, a.AdjustedXp, built.FullyMatched, built.Note,
                        built.PartyLevels, MonsterGrouping.Group(a.Monsters));
                },
                name: "build_encounter",
                description: "Build a combat encounter for a target difficulty " +
                    "(Trivial/Easy/Medium/Hard/Deadly) and optional theme, for the signed-in user's " +
                    "party (from the caller's own campaignId). Builds swarms — a strong anchor plus " +
                    "multiples of cheaper monsters — returned grouped as {monster, count} (e.g. one " +
                    "hobgoblin leading eight goblins). Rated by the same math as rate_encounter, so a " +
                    "built encounter and a subsequent rate_encounter call agree on its difficulty. " +
                    "edition is \"2014\" or \"2024\". Optional maxCr/minCr constrain the candidate " +
                    "monsters' CR range; when omitted, a sensible CR ceiling/floor is derived from the " +
                    "target difficulty band."));

            toolList.Add(AIFunctionFactory.Create(
                (long heroSnapshotId, string? targetClass, bool? considerDip, CancellationToken toolCt) =>
                    levelUpService.PlanForUserAsync(
                        heroSnapshotId, userId, targetClass, considerDip ?? false, toolCt),
                name: "plan_level_up",
                description: "Plan the next level-up for a hero snapshot the signed-in user owns. Returns, " +
                    "for each way to advance (each existing class, plus legal new-class dips when considerDip " +
                    "is true), the rule-grounded delta (HP, proficiency bonus, spell-slot change, features " +
                    "gained) and the real cited options for each open choice (ability-score-or-feat, subclass, " +
                    "new spells). RECOMMEND a specific pick with reasons that reference the character's own " +
                    "sheet, but ONLY from the options returned here — never invent a feat, subclass, or spell. " +
                    "targetClass (optional) limits advice to one class."));

            toolList.Add(AIFunctionFactory.Create(
                (string className, string concept, int? targetLevel, CancellationToken toolCt) =>
                    buildRecommender.RecommendBuildOptionsAsync(className, concept, targetLevel, toolCt),
                name: "recommend_build",
                description: "Recommend a single-class D&D character build for a text concept (e.g. 'a tanky " +
                    "dwarf who controls the battlefield'). YOU pick the class that best fits the concept and " +
                    "pass it as className plus the concept; if the result says the class is not in the corpus " +
                    "(ClassInCorpus false), pick a different class from its availableClasses list and call again. " +
                    "Then recommend a subclass, key feats, and signature spells STRICTLY from the returned cited " +
                    "options, plus ability-score priorities from the returned save proficiencies / spellcasting " +
                    "ability, and explain why it fits the concept. Never invent a subclass, feat, or spell. " +
                    "Single-class only — if the concept implies multiclassing, recommend the primary class and " +
                    "note a dip direction."));

            toolList.Add(AIFunctionFactory.Create(
                (long heroSnapshotId, CancellationToken toolCt) =>
                    critiqueService.CritiqueForUserAsync(heroSnapshotId, userId, toolCt),
                name: "critique_build",
                description: "Review a hero snapshot the signed-in user owns and critique the build. Returns " +
                    "grounded findings — untaken choices (a subclass not chosen, class features not recorded), " +
                    "stat inconsistencies (recorded save DC/attack/slots vs computed), and ability misalignment. " +
                    "Frame these into a critique anchored to the findings; do NOT invent problems or free-judge. " +
                    "Where a finding suggests it, hand off: an untaken subclass/feature → suggest plan_level_up; " +
                    "an ability misalignment → suggest recommend_build."));

            toolList.Add(AIFunctionFactory.Create(
                (long campaignId, string question, string? edition, CancellationToken toolCt) =>
                    settingLoreService.AskForUserAsync(
                        userId, campaignId, question,
                        string.IsNullOrWhiteSpace(edition) ? (DndVersion?)null : ParseEdition(edition),
                        toolCt),
                name: "ask_setting_lore",
                description: "Answer a lore/worldbuilding question for one of the signed-in user's own " +
                    "campaigns, scoped to that campaign's SETTING sources. Pass the campaignId and the " +
                    "question. Returns cited passages retrieved ONLY from the campaign's setting books " +
                    "(plus core rules). Compose your answer STRICTLY from the returned passages and CITE " +
                    "each (source book + section); if no passages are returned, say the campaign's setting " +
                    "sources don't cover it — never invent world lore. edition is \"2014\" or \"2024\"."));

            toolList.Add(AIFunctionFactory.Create(
                (string question, string[]? ruleTopics, string? edition, CancellationToken toolCt) =>
                    rulesAdjudicationService.AskAsync(
                        question,
                        ruleTopics,
                        string.IsNullOrWhiteSpace(edition) ? (DndVersion?)null : ParseEdition(edition),
                        toolCt),
                name: "ask_rules",
                description: "Answer a D&D RULES question (including multi-rule interactions like " +
                    "'can I grapple a creature that's already prone?'). Identify the DISTINCT rules the " +
                    "question involves and pass them as ruleTopics (e.g. [\"grappling\", \"prone condition\"]) " +
                    "so each rule is grounded on its own retrieval; omit ruleTopics for a simple single-rule " +
                    "question. Returns cited rule passages retrieved ONLY from the core rulebooks, grouped by " +
                    "topic. Compose your ruling STRICTLY from the returned passages: NAME each rule you " +
                    "combine and CITE it (source book + section); where the rules don't explicitly resolve an " +
                    "interaction, say so and distinguish rules-as-written from a DM ruling; if no passages are " +
                    "returned, say the rules don't directly cover it — never invent a rule. Not tied to any " +
                    "campaign or character. edition is optional (\"2014\"/\"2024\"); omit to search all editions."));

            toolList.Add(AIFunctionFactory.Create(
                (string concept, string archetype, double? maxCr, CancellationToken toolCt) =>
                    npcGenerationService.GenerateAsync(concept, archetype, maxCr, toolCt),
                name: "generate_npc",
                description: "Generate an NPC for a scene from a concept (e.g. 'a shifty Sharn dockworker'). " +
                    "YOU pick the stat-block archetype that best fits the concept and pass it as archetype " +
                    "(e.g. Spy, Commoner, Guard, Bandit Captain, Veteran); the tool returns that archetype's " +
                    "REAL stat block (CR/HP/ability scores + the rendered block). Compose the NPC's name, " +
                    "personality, appearance, and a hook to fit the concept, but take ALL mechanical stats " +
                    "from the returned block and CITE it (source book) — NEVER invent stat numbers. If the " +
                    "result says the archetype is not in the corpus (archetypeInCorpus false), pick a different " +
                    "one from availableArchetypes and call again. Optional maxCr caps the archetype's power. " +
                    "Not tied to any campaign or character."));

            toolList.Add(AIFunctionFactory.Create(
                (long campaignId, string theme, string? difficulty, string npcArchetype, CancellationToken toolCt) =>
                    sessionPrepService.PrepForUserAsync(
                        userId, campaignId, theme, ParseDifficulty(difficulty), npcArchetype,
                        DndVersion.Edition2014, toolCt),
                name: "prep_session",
                description: "Prep a game session for the signed-in user's OWN campaign (campaignId). Pass a " +
                    "theme (e.g. 'Sharn intrigue'), an optional difficulty (Trivial/Easy/Medium/Hard/Deadly), " +
                    "and the NPC stat-block archetype you pick to fit the theme (e.g. Spy, Guard, Cult Fanatic). " +
                    "Returns a cohesive packet: an encounter built for the campaign's party, a grounded NPC, and " +
                    "setting lore hooks scoped to the campaign's world. Compose a session outline STRICTLY from " +
                    "the returned pieces — cite the encounter's monsters, the NPC's real stat block, and the " +
                    "lore hooks; if the NPC archetype isn't in the corpus (npc.archetypeInCorpus false), re-pick " +
                    "from npc.availableArchetypes. Never invent stat numbers or world lore."));
        }

        History.Add(new ChatMessage(ChatRole.User, userMessage));
        await PersistAsync("user", userMessage);
        try
        {
            // Build a per-request message list: [System(persona), ...History]
            // The system message is NOT added to History and is NOT persisted.
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, personaProvider.GetPersonaText()),
            };
            messages.AddRange(History);

            var response = await chatClient.GetResponseAsync(
                messages,
                new ChatOptions { Tools = [.. toolList] },
                ct);
            var reply = response.Text ?? string.Empty;
            History.Add(new ChatMessage(ChatRole.Assistant, reply));
            await PersistAsync("assistant", reply);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Failure is surfaced to the caller (UI banner); no assistant bubble is injected.
            return false;
        }
    }

    /// <summary>
    /// Clears the in-memory conversation and permanently deletes the signed-in user's
    /// persisted chat turns so the conversation does not replay on reload.
    /// </summary>
    public async Task ClearAsync()
    {
        History.Clear();
        var idClaim = httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (long.TryParse(idClaim, out var userId))
            await chatRepository.DeleteConversationAsync(userId);
    }

    private async Task PersistAsync(string role, string content)
    {
        try
        {
            var idClaim = httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!long.TryParse(idClaim, out var userId)) return;
            await chatRepository.AddAsync(new ChatTurn
            {
                UserId = userId,
                Role = role,
                Content = content,
                CreatedAt = DateTime.UtcNow,
            });
        }
        catch
        {
            // Persistence of chat history must never break the chat response.
        }
    }

    /// <summary>
    /// Parses a caller-supplied edition string ("2014"/"2024", tolerant of the enum name form
    /// e.g. "Edition2014") case-insensitively. Defaults to <see cref="DndVersion.Edition2014"/>
    /// for anything unrecognized, since most of the corpus and existing campaigns are 2014-edition.
    /// </summary>
    private static DndVersion ParseEdition(string? edition) => edition?.Trim().ToLowerInvariant() switch
    {
        "2024" or "edition2024" => DndVersion.Edition2024,
        _ => DndVersion.Edition2014,
    };

    /// <summary>
    /// Parses a caller-supplied difficulty string (case-insensitive) into a <see cref="Difficulty"/>.
    /// Defaults to <see cref="Difficulty.Medium"/> for anything unrecognized, since build_encounter
    /// requires some target and "Medium" is the least surprising default.
    /// </summary>
    private static Difficulty ParseDifficulty(string? difficulty) => difficulty?.Trim().ToLowerInvariant() switch
    {
        "trivial" => Difficulty.Trivial,
        "easy" => Difficulty.Easy,
        "hard" => Difficulty.Hard,
        "deadly" => Difficulty.Deadly,
        _ => Difficulty.Medium,
    };
}