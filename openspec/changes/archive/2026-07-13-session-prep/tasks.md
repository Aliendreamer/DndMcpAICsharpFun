## 1. SessionPrepService + SessionPrepPacket + DI

- [ ] 1.1 (TDD, real Postgres) Tests (mirror `EncounterDesignServiceTests`: real `CampaignRepository`/`HeroRepository` over `PostgresFixture`; construct `EncounterDesignService`/`SettingLoreService`/`NpcGenerationService` with substitute retrieval/monster-source so the three sub-results are controllable): a foreign campaign → throws (encounter ownership check, before any NPC/lore work); an owned campaign with heroes → `SessionPrepPacket` with the built encounter + generated NPC + lore hooks + the theme; the lore question passed to the lore service is derived from the theme; a Generic (no-setting) campaign → empty hooks but encounter + NPC still present
- [ ] 1.2 `Features/SessionPrep/`: `SessionPrepPacket(string Theme, BuiltEncounter Encounter, GeneratedNpc Npc, SettingLoreResult LoreHooks)`; `SessionPrepService(EncounterDesignService, NpcGenerationService, SettingLoreService)` with `PrepForUserAsync(userId, campaignId, string theme, Difficulty difficulty, string npcArchetype, DndVersion edition, ct)` — encounter FIRST (ownership gate), then NPC (concept=theme, archetype=npcArchetype), then lore (`LoreQuestion(theme)` derived), assemble the packet. No LLM call. A private `LoreQuestion(theme)` template

## 2. DI wiring

- [ ] 2.1 Register `SessionPrepService`; `AddDndChat` pulls in its `Add*` (mirror `AddNpc`/`AddRules`); confirm it's in the `FullContainerScopeValidationTests` graph (transitive via `AddDndChat`); run the scope-validation filter green

## 3. prep_session chat tool

- [ ] 3.1 Register `prep_session(campaignId, theme, difficulty?, npcArchetype)` in `DndChatService` (authenticated block; closes over session `userId`; takes `campaignId`; NO `userId` arg); parse difficulty/edition via the existing `ParseDifficulty`/`ParseEdition` helpers (default difficulty Medium, edition 2014); description binds the contract (compose the outline from the returned packet — encounter + NPC stats + cited hooks, cite each; re-pick the archetype from availableArchetypes on a miss)
- [ ] 3.2 (TDD) Guard tests: `prep_session` present-when-authenticated / absent-when-not; schema exposes NO `userId`; a routing test that invoking it with a foreign/absent `campaignId` throws (ownership reached the encounter sub-service) — mirror the `build_encounter` guard-throw test

## 4. Verification

- [ ] 4.1 Build 0/0 + FULL `dotnet test` green (real Postgres + Qdrant) — the FULL suite, not a filter
- [ ] 4.2 Rebuild app container; live smoke (Ollama): with the `test` user's Eberron campaign (has heroes), "prep a Sharn intrigue session for my Eberron campaign" → a cohesive outline citing a built encounter (party-scaled), a fitting NPC with real stats, and ERLW setting hooks; a foreign campaign path is rejected. No new UI → no overflow gate
- [ ] 4.3 Final opus whole-branch review; on READY, refresh the `companion_roadmap` memory
