## 1. NpcPartyTemplates + result records

- [ ] 1.1 Add to `Features/Npc/GeneratedNpc.cs`: `public sealed record NpcPartyMember(string Role, GeneratedNpc Npc);` and `public sealed record GeneratedNpcParty(string Theme, string Template, IReadOnlyList<NpcPartyMember> Members);`.
- [ ] 1.2 Create `Features/Npc/NpcPartyTemplates.cs`: an ordered template list, each `(string Name, string[] Keywords, IReadOnlyList<(string Role, string Archetype)> Roster)`, plus a `Default` roster. Rosters (leader first) per design.md D1 — criminal/military/cult/noble/arcane + Default. `Resolve(string theme)` → lowercases theme, returns the first template with any keyword as a substring, else `("default", DefaultRoster)`.
- [ ] 1.3 Unit test `NpcPartyTemplatesTests`: criminal keyword → Bandit Captain leader; unmatched → Default (Veteran leader), never empty; **every roster archetype (all templates + Default) ∈ `NpcArchetypes.Common`** (guards a typo pointing at a non-grounded name); first-keyword-wins is deterministic.
- [ ] 1.4 Run tests; commit `feat(npc): NpcPartyTemplates deterministic theme→ensemble table`.

## 2. GeneratePartyAsync on NpcGenerationService

- [ ] 2.1 Add `public async Task<GeneratedNpcParty> GeneratePartyAsync(string theme, CancellationToken ct)` to `NpcGenerationService`: `Resolve` the template, then for each `(role, archetype)` call the existing `GenerateAsync(concept: role, archetype, maxCr: null, ct)` and wrap as `NpcPartyMember(role, npc)`; return `GeneratedNpcParty(theme, templateName, members)` (members in roster order).
- [ ] 2.2 Unit test `NpcGenerationServicePartyTests` with a fake `IEntityRetrievalService` (mirror the existing `NpcGenerationService` test's fake): a themed call returns one member per roster entry, in order, each with role + a grounded `GeneratedNpc` (StatBlock populated, ArchetypeInCorpus true); a fake that returns no hit for one archetype → that member's `ArchetypeInCorpus` false and the party still returns all members.
- [ ] 2.3 Run tests, full suite green; commit `feat(npc): GeneratePartyAsync grounds a themed NPC ensemble`.

## 3. generate_npc_party chat tool

- [ ] 3.1 Register `generate_npc_party` in `Features/Chat/DndChatService.cs` next to `generate_npc` (authenticated block, ownership-free): `(string theme, CancellationToken toolCt) => npcGenerationService.GeneratePartyAsync(theme, toolCt)`. Description per design.md D4 (themed ensemble of grounded NPCs; name/flavor to theme; take + cite all stats from the returned blocks; never invent; drop/replace a not-in-corpus member; not tied to any campaign).
- [ ] 3.2 Add the tool to BOTH per-user chat-tool guard surfaces (the `DndChatServiceTests` "no tool schema exposes userId" name-filter list AND the authenticated-present / unauthenticated-absent presence lists) so the guard isn't vacuous — mirror how `generate_npc` is covered.
- [ ] 3.3 Tool test: authenticated tool list contains `generate_npc_party` with exactly one string param (`theme`), no `userId`; invoking it (via the real registered delegate off `client.LastOptions.Tools`, like the other chat-tool tests) with `theme:"temple cult"` over a fake retrieval returns the cult ensemble with grounded members.
- [ ] 3.4 Run tests, FULL suite green; commit `feat(chat): generate_npc_party single-param ownership-free tool`.

## 4. Live smoke + finish

- [ ] 4.1 Rebuild the app image (`docker compose up -d --build app`), wait healthy. Live smoke (Playwright chat, `test`/`test`): ask for a themed cast (e.g. "give me a criminal gang for a Sharn heist"). Confirm qwen3 INVOKES `generate_npc_party` (1-param → should be reliable where 4-param `prep_session` wasn't), and the reply is a leader + supporting cast each with a real cited stat block, flavored to the theme. If the chat smoke is flaky (qwen3 latency / browser circuit drop — check `ChatTurns` for a missing assistant row), fall back to validating the tool/service layer directly.
- [ ] 4.2 If the smoke surfaces a durable lesson (e.g. 1-param party tool invokes reliably where multi-param didn't — evidence for/against the model upgrade), add it to `.claude/skills/dev-flow/SKILL.md`.
