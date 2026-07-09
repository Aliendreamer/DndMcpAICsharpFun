using System.Security.Claims;

using DndMcpAICsharpFun.Domain;
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
    EncounterDesignService encounterService)
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
                (long? campaignId, int[]? partyLevels, string[] monsters, string edition, CancellationToken toolCt) =>
                    encounterService.RateForUserAsync(
                        userId, campaignId, partyLevels, monsters, ParseEdition(edition), toolCt),
                name: "rate_encounter",
                description: "Rate a combat encounter's difficulty (Trivial/Easy/Medium/Hard/Deadly) " +
                    "for the signed-in user's party. The party comes from the caller's own campaign " +
                    "(campaignId) or an explicit partyLevels list (one level per party member); " +
                    "campaignId wins ownership-checked access, partyLevels overrides it when supplied. " +
                    "monsters is a list of entity ids or free-text names to look up. edition is " +
                    "\"2014\" or \"2024\"."));

            toolList.Add(AIFunctionFactory.Create(
                (long? campaignId, string difficulty, string edition, string? theme,
                    double? maxCr, double? minCr, CancellationToken toolCt) =>
                    encounterService.BuildForUserAsync(
                        userId, campaignId, partyLevels: null, ParseDifficulty(difficulty),
                        ParseEdition(edition), theme, crLte: maxCr, crGte: minCr, toolCt),
                name: "build_encounter",
                description: "Build a combat encounter for a target difficulty " +
                    "(Trivial/Easy/Medium/Hard/Deadly) and optional theme, for the signed-in user's " +
                    "party (from the caller's own campaignId). Rated by the same math as " +
                    "rate_encounter, so a built encounter and a subsequent rate_encounter call agree " +
                    "on its difficulty. edition is \"2014\" or \"2024\". Optional maxCr/minCr let a DM " +
                    "constrain the candidate monsters' CR range (e.g. \"nothing above CR 5\"); when " +
                    "omitted, a sensible CR ceiling/floor is derived automatically from the target " +
                    "difficulty band."));
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