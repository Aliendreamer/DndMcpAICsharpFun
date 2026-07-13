## Why

A DM needs an NPC for a scene — "a shifty Sharn dockworker", "a grizzled guard captain" — with real,
runnable stats, not invented numbers. The corpus already has the full MM NPC roster (Guard, Spy,
Noble, Commoner, Bandit, Veteran, …) as Monster entities. This adds a grounded NPC generator that
anchors to a real stat block (its numbers cited from the corpus) and lets the persona invent only the
flavor (name, personality, hook) — never the mechanics.

## What Changes

- **`generate_npc(concept, archetype, maxCr?)` chat tool** — per-session, **not ownership-gated** (no
  campaign/user data). The chat LLM picks the NPC stat-block `archetype` that best fits the `concept`;
  the tool fetches that Monster entity from `dnd_entities` and returns its **real stat block** (name,
  CR, AC, HP, ability scores, key traits/actions, `sourceBook` citation, plus the rendered
  `canonicalText`). If the archetype isn't in the corpus, it returns `ArchetypeInCorpus=false` + a
  curated list of available NPC archetypes so the LLM re-picks. Optional `maxCr` rejects an
  over-powered pick (returns the not-found/too-strong result with the roster). The persona then
  invents the name/personality/appearance/hook around the grounded stats and cites the stat block —
  never inventing stat numbers.
- **`NpcGenerationService.GenerateAsync(concept, archetype, maxCr, ct)`** — resolve the archetype to a
  real Monster entity (exact-name-match over entity search) → project to a grounded `NpcStatBlock`;
  not-found / over-`maxCr` → `ArchetypeInCorpus=false` + `AvailableArchetypes`. Mirrors
  `BuildRecommenderService` (LLM picks, service validates, suggests on miss). No LLM call.
- **`NpcArchetypes.Common`** — a curated roster of the common MM NPC stat-block names for the
  suggestion list.

## Capabilities

### New Capabilities

- `npc-generation`: the curated NPC archetype roster, the `NpcGenerationService` that grounds an NPC
  to a real corpus stat block, and the ownership-free `generate_npc` chat tool.

### Modified Capabilities

<!-- None. -->

## Impact

- **Code:** new `Features/Npc/` (`NpcArchetypes`, `NpcGenerationService`, `NpcStatBlock`/`GeneratedNpc`
  records). `Features/Chat/DndChatService.cs` — register `generate_npc`, inject the service; DI pulled
  into `AddDndChat` + validated by `FullContainerScopeValidationTests`. Reuses `IEntityRetrievalService`
  and the existing monster CR reader.
- **No** migration, HTTP route, `.http`/`.insomnia`, or shared-key MCP change (chat-only tool).
