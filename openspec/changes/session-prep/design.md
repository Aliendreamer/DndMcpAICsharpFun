## Context

Three grounded, campaign-aware surfaces are shipped: `EncounterDesignService.BuildForUserAsync(userId,
campaignId, …, theme, …)` → `BuiltEncounter` (ownership-gated, party from the campaign's heroes);
`NpcGenerationService.GenerateAsync(concept, archetype, maxCr, ct)` → `GeneratedNpc` (grounded to a
real stat block, ownership-free); `SettingLoreService.AskForUserAsync(userId, campaignId, question,
version, ct)` → `SettingLoreResult` (ownership-gated, scoped to the campaign's setting). Both
campaign-scoped services do their own `CampaignRepository.GetByIdAsync(id, userId)` ownership check.

## Goals / Non-Goals

**Goals:**

- One call → a cohesive, campaign-scoped prep packet (encounter + NPC + setting hooks) whose pieces
  all fit this party and world.
- Reuse the three shipped services verbatim — no re-implementation, each keeps its grounding +
  ownership.
- Honest degradation per piece; ownership enforced by the campaign-scoped calls.

**Non-Goals:**

- A new LLM call (the persona weaves the packet; the LLM supplies theme/difficulty/archetype).
- Party/group of NPCs; multiple encounters; a fully tool-authored session narrative — v2.
- Re-implementing any grounding (the sub-services own it); no new retrieval/entity logic.
- Migration, HTTP/MCP surface.

## Decisions

### D1 — Compose the three shipped services; encounter first (ownership gate)

`SessionPrepService(EncounterDesignService encounters, NpcGenerationService npcs, SettingLoreService
lore)` with `PrepForUserAsync(userId, campaignId, theme, difficulty, npcArchetype, edition, ct)`:

1. `var encounter = await encounters.BuildForUserAsync(userId, campaignId, partyLevels: null,
   ParseDifficulty(difficulty), edition, theme, crLte: null, crGte: null, ct);` — **first**, so a
   foreign/empty campaign throws here and short-circuits the whole prep (no NPC/lore work on an
   unauthorized campaign).
2. `var npc = await npcs.GenerateAsync(concept: theme, archetype: npcArchetype, maxCr: null, ct);`
3. `var hooks = await lore.AskForUserAsync(userId, campaignId, LoreQuestion(theme), edition, ct);`
4. `return new SessionPrepPacket(theme, encounter, npc, hooks);`

`difficulty`/`edition` parsing mirrors the existing chat-tool parsing (or accept the enums directly
and let the tool parse). The service adds no grounding of its own.

### D2 — Derived lore question

`LoreQuestion(theme)` = a fixed template, e.g. `$"factions, locations, and plot hooks related to
{theme}"`. Keeps the LLM's inputs to what it's good at (theme/difficulty/archetype) while the
setting-scoped hooks fall out of the shipped lore service. A Generic campaign yields unscoped hooks (corpus-wide, not setting-restricted) —
the packet still carries the encounter + NPC.

### D3 — Reuse the sub-result types

`SessionPrepPacket(string Theme, BuiltEncounter Encounter, GeneratedNpc Npc, SettingLoreResult
LoreHooks)` — the LLM receives the full grounded sub-results (the encounter's monsters/XP, the NPC's
real stat block or re-pick roster, the cited lore passages) and composes the outline.

### D4 — Per-user tool, ownership delegated

`prep_session(campaignId, theme, difficulty?, npcArchetype)` closes over the session `userId`; the
ownership check is delegated to `BuildForUserAsync`/`AskForUserAsync` (both verify the campaign is the
caller's). The tool exposes no `userId` argument. Registered in the authenticated block.

## Risks / Trade-offs

- **[Thin over three tools]** → the net-new value is the campaign-scoped cohesion + one grounded
  packet; the sub-services own the grounding, so this is deliberately a composition, not new logic.
- **[Ordering couples the ownership gate to the encounter call]** → building the encounter first is
  the intended gate; if a future refactor reorders, the ownership check must stay first (an explicit
  `GetByIdAsync` guard in the service is an alternative, but delegating avoids a duplicate check).
- **[A sub-piece degrades]** → each returns its honest result (empty hooks / re-pick roster); the
  packet still assembles, and the persona notes gaps. Only a foreign/empty campaign (encounter throw)
  fails the whole call, which is correct.
- **[Testability of sealed sub-services]** → `SessionPrepService` takes the three concrete services;
  tests construct them over real Postgres (campaign ownership/party, as `EncounterDesignServiceTests`
  does) + substitute retrieval (monster/entity/rag) driving the sub-results — no interface refactor.
