using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Encounters;
using DndMcpAICsharpFun.Features.Lore;
using DndMcpAICsharpFun.Features.Npc;

namespace DndMcpAICsharpFun.Features.SessionPrep;

/// <summary>
/// Composes the shipped encounter, NPC, and setting-lore surfaces into one campaign-scoped prep
/// packet. Adds no grounding of its own — each sub-service keeps its grounding and ownership check.
/// The encounter build runs FIRST so its ownership check gates the whole prep. No LLM call.
/// </summary>
public sealed class SessionPrepService(
    EncounterDesignService encounters, NpcGenerationService npcs, SettingLoreService lore)
{
    public async Task<SessionPrepPacket> PrepForUserAsync(
        long userId, long campaignId, string theme, Difficulty difficulty, string npcArchetype,
        DndVersion edition, CancellationToken ct)
    {
        // First — its ownership check (foreign/empty campaign → throws) gates the whole prep.
        // Encounter is a party-appropriate baseline (theme: null) — filtering monsters by the
        // narrative theme's keyword tends to match zero monsters (e.g. "Sharn intrigue"); the
        // persona re-skins/re-themes the baseline encounter to fit the session's narrative theme.
        var encounter = await encounters.BuildForUserAsync(
            userId, campaignId, partyLevels: null, difficulty, edition, theme: null,
            crLte: null, crGte: null, ct);

        var npc = await npcs.GenerateAsync(concept: theme, archetype: npcArchetype, maxCr: null, ct);

        var hooks = await lore.AskForUserAsync(userId, campaignId, LoreQuestion(theme), edition, ct);

        return new SessionPrepPacket(theme, encounter, npc, hooks);
    }

    private static string LoreQuestion(string theme) =>
        $"factions, locations, and plot hooks related to {theme}";
}
