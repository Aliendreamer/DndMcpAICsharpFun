## Context

`generate_npc` (shipped) resolves ONE archetype to a real Monster stat block via anti-fuzzy name-match
(`NpcGenerationService.GenerateAsync`), with the LLM picking the archetype and flavoring name/hook. A
"cast of NPCs" needs several such NPCs at once. The binding constraint is qwen3: multi-param and array
tool calls fail its MEAI binding (the 4-param `prep_session` is chat-flaky and was deferred), so the
party tool must be **single-string-param**. A second hard lesson (session-prep) is that a narrative
theme must NEVER be used as a monster/keyword FILTER — it matches ~0 and collapses to empty. Both
constraints shape the design below.

## Goals / Non-Goals

**Goals:** one reliable call → a themed ensemble of grounded NPCs (leader + supporting), each anchored
to a real stat block; theme drives FLAVOR + deterministic template selection, never a fuzzy filter.

**Non-Goals:** LLM-specified roles/counts (array params — qwen3-flaky); setting/campaign coupling
(ownership + params); a Monster-entity search from the theme (the session-prep bug); fattening
`prep_session`; any new retrieval/migration/HTTP surface.

## Decisions

### D1 — `NpcPartyTemplates`: deterministic theme→ensemble table

`Features/Npc/NpcPartyTemplates.cs`: an ordered list of templates, each `(string[] Keywords, IReadOnlyList<(string Role, string Archetype)> Roster)`,
plus a `Default` roster. `Resolve(string theme)` lowercases the theme and returns the first template
whose any-keyword is a substring of the theme, else `Default`. All archetypes MUST be members of
`NpcArchetypes.Common` (guaranteed-grounded MM roster) — a unit test asserts this. Rosters (leader
first):

- criminal/heist/gang/thieves/smuggl → Bandit Captain, Thug, Thug, Spy
- military/guard/watch/soldier/mercenary → Veteran, Guard, Guard, Scout
- cult/temple/zealot/heretic → Cult Fanatic, Cultist, Cultist, Acolyte
- noble/court/political/intrigue/house → Noble, Guard, Guard, Spy
- arcane/mage/wizard/arcanist → Mage, Acolyte, Guard, Guard
- Default → Veteran, Guard, Guard, Commoner

### D2 — `GeneratePartyAsync` reuses the per-member grounding

`NpcGenerationService.GeneratePartyAsync(string theme, CancellationToken ct)`:

1. `var (name, roster) = NpcPartyTemplates.Resolve(theme);`
2. For each `(role, archetype)` in the roster, resolve the grounded NPC by archetype NAME — reuse
   `GenerateAsync(concept: role, archetype, maxCr: null, ct)` (which already does the anti-fuzzy
   name-match + not-in-corpus flag + full-envelope stat-block render).
3. Return `GeneratedNpcParty(theme, TemplateName, members)` where each member is
   `NpcPartyMember(role, generatedNpc)`.

Members resolve sequentially (a handful of retrievals; no concurrency needed). A member whose archetype
somehow isn't in the corpus surfaces `Npc.ArchetypeInCorpus=false` (its `AvailableArchetypes` menu is
already populated) — the party still returns; the LLM is told to skip/replace a not-in-corpus member.

### D3 — Result records

`Features/Npc/GeneratedNpc.cs` gains:
`public sealed record NpcPartyMember(string Role, GeneratedNpc Npc);`
`public sealed record GeneratedNpcParty(string Theme, string Template, IReadOnlyList<NpcPartyMember> Members);`

### D4 — `generate_npc_party(theme)` tool — one string param, ownership-free

Registered in `DndChatService` alongside `generate_npc`, in the authenticated block but taking **no**
`userId`/`campaignId`. Signature `(string theme, CancellationToken toolCt)`. Description: returns a
themed ENSEMBLE of grounded NPCs (a leader + supporting cast), each with a REAL stat block; compose
each one's name/personality/hook to fit the theme, take ALL mechanical stats from the returned blocks
and CITE them (source book), never invent stats; if a member is not in the corpus
(`npc.archetypeInCorpus` false) drop or replace it. Not tied to any campaign.

## Risks / Trade-offs

- **[Fixed rosters ignore theme mechanically]** → intended: for a CAST, flavor matters more than stat
  variety, and the LLM reskins freely; deterministic rosters guarantee grounding + qwen3-safety. The
  keyword templates give enough theme responsiveness (criminal vs military vs cult) without a fuzzy
  search.
- **[Template keyword miss]** → falls back to the Default ensemble (never empty), and the LLM still
  reflavors — no silent empty result.
- **[Maintaining the template table]** → small and static, like `NpcArchetypes`/`SettingCatalog`;
  grows as needed. The "archetypes ⊆ Common" test prevents a typo pointing at a non-grounded name.
- **[qwen3 still mis-invokes a 1-param tool]** → far less likely than the 4-param case; the live smoke
  confirms. If even 1-param is unreliable, that is evidence for the parked model upgrade, not a design
  flaw here.
