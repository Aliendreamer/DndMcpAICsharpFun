## Why

A DM prepping a session wants an encounter, an NPC, and setting hooks that all fit *this* campaign's
party and world — currently three separate tool calls with no guaranteed cohesion. The atomic,
grounded pieces now all exist (`build_encounter`, `generate_npc`, `ask_setting_lore`). This adds the
orchestration capstone: one call that composes them into a single cohesive, campaign-scoped prep
packet.

## What Changes

- **`prep_session(campaignId, theme, difficulty?, npcArchetype)` chat tool** — per-user (closes over
  the session user id, takes a `campaignId`; ownership is enforced by the campaign-scoped sub-services
  it calls). For the caller's campaign it builds an encounter for the party (themed, at the
  difficulty), generates a fitting NPC (the LLM-picked archetype), and pulls setting lore hooks
  (scoped to the campaign's setting), and returns them as one packet. The persona weaves the grounded
  packet into a session outline. Each piece degrades honestly (no setting → unscoped hooks; unknown
  archetype → the re-pick roster; empty/foreign campaign → the existing throw).
- **`SessionPrepService.PrepForUserAsync(userId, campaignId, theme, difficulty, npcArchetype, edition,
  ct)`** — composes `EncounterDesignService.BuildForUserAsync` (first, so its ownership check gates
  the whole prep), `NpcGenerationService.GenerateAsync`, and `SettingLoreService.AskForUserAsync`
  (with a lore question derived from the theme). Reuses the three shipped services verbatim — each
  keeps its own grounding + ownership. No new LLM call.
- **`SessionPrepPacket`** — reuses `BuiltEncounter` / `GeneratedNpc` / `SettingLoreResult` as the
  grounded sub-results.

## Capabilities

### New Capabilities

- `session-prep`: the `SessionPrepService` that composes the shipped encounter / NPC / setting-lore
  surfaces into one campaign-scoped grounded packet, and the per-user `prep_session` chat tool.

### Modified Capabilities

<!-- None. -->

## Impact

- **Code:** new `Features/SessionPrep/` (`SessionPrepService`, `SessionPrepPacket`). `Features/Chat/
  DndChatService.cs` — register `prep_session`, inject the service; DI pulled into `AddDndChat` +
  validated by `FullContainerScopeValidationTests`. Depends on the shipped `EncounterDesignService`,
  `NpcGenerationService`, `SettingLoreService`.
- **No** migration, HTTP route, `.http`/`.insomnia`, or shared-key MCP change (chat-only tool). The
  ownership gate is enforced by the campaign-scoped sub-services (the encounter build runs first).
