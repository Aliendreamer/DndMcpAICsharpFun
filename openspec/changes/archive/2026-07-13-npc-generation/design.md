## Context

`dnd_entities` holds the MM NPC roster (Guard/Spy/Noble/Commoner/Bandit/Veteran/… — feasibility-probed
2026-07-13) as Monster entities with full `MonsterFields` (Ac/Hp/Str.../Cr/Trait/Action) and a rendered
`CanonicalText`. `IEntityRetrievalService.SearchDiagnosticAsync` does name/semantic entity search;
`BuildRecommenderService` (shipped) established the "LLM picks → service validates → grounded options /
suggest-on-miss → persona composes" pattern this reuses. `dnd_entities.sourceBook` is the 5etools KEY
(`"MM"`/`"PHB"`) — distinct from `dnd_blocks`' display names (a DATA-INVARIANT note; it only affects the
citation string, not the fetch).

## Goals / Non-Goals

**Goals:**

- Generate an NPC whose MECHANICS are a real corpus stat block (cited); only the flavor is invented.
- Reuse the `recommend_build` grounding pattern (LLM picks archetype, service validates, suggests on
  miss).
- Keep it small: standalone, single NPC, ownership-free.

**Non-Goals:**

- The tool inventing flavor (name/personality) — that's the persona's job (LLMs do reskin well).
- Setting-aware NPC names/hooks (v2, via `ask_setting_lore` / session-prep composition).
- A party/group of NPCs; a fully tool-assembled structured NPC.
- Ownership/campaign coupling; migration; HTTP/MCP surface.

## Decisions

### D1 — LLM picks the archetype; service validates + grounds it

`generate_npc(concept, archetype, maxCr?)`: the chat LLM reads the concept and picks the stat-block
`archetype` (e.g. shifty dockworker → "Spy" or "Commoner"). `NpcGenerationService.GenerateAsync`
resolves `archetype` to a real Monster entity via `SearchDiagnosticAsync(QueryText=archetype,
Type=Monster, TopK=1)` **with an exact (normalized) name match** on the top hit — no fuzzy acceptance,
so a bad pick doesn't silently return an unrelated block. On resolve → project to `NpcStatBlock`. On
miss (or when the resolved CR exceeds `maxCr`) → `ArchetypeInCorpus=false` (or a too-strong flag) +
`AvailableArchetypes = NpcArchetypes.Common`, so the LLM re-picks. Mirrors `BuildRecommenderService`.

*Alternative considered:* a concept-based cited menu (retrieve top-N NPC blocks by the concept, LLM
picks). Rejected for the first slice — "shifty dockworker" semantic-searches poorly against
stat-block prose, whereas the LLM reliably maps concept→archetype; the validate-by-name path is more
robust and matches `recommend_build`.

### D2 — `NpcStatBlock` = grounded fields + the rendered block, cited

`NpcStatBlock(Name, SourceBook, Cr, Ac, Hp, Str, Dex, Con, Int, Wis, Cha, CanonicalText)` — the key
structured fields the persona summarizes plus the entity's `CanonicalText` (the full rendered stat
block) as the authoritative grounded base. `Cr` is read via the existing monster CR reader (reused
from encounters) for the `maxCr` gate. `GeneratedNpc(Concept, Archetype, NpcStatBlock? StatBlock,
bool ArchetypeInCorpus, IReadOnlyList<string> AvailableArchetypes)`.

### D3 — Curated `NpcArchetypes.Common` roster

A fixed list of the common MM NPC stat-block names (Guard, Spy, Noble, Commoner, Bandit, Cultist,
Priest, Mage, Veteran, Thug, Acolyte, Bandit Captain, Knight, Scout, Assassin, Berserker, Gladiator,
Archmage, …) — the suggestion list on a miss. The service can fetch ANY monster by name (not limited
to the roster); the roster is only the fallback menu.

### D4 — Ownership-free tool

`generate_npc` closes over nothing user-specific (no `userId`/`campaignId`); registered in the
authenticated block. Description binds the contract: pick the fitting archetype; the tool returns real
stats; invent name/personality/appearance/hook around them and CITE the stat block; never invent stat
numbers; if not in the corpus, pick from `availableArchetypes`.

## Risks / Trace-offs

- **[LLM picks an archetype not in the corpus]** → exact-name-match miss → `ArchetypeInCorpus=false` +
  roster → LLM re-picks. Never returns an unrelated block (no fuzzy acceptance).
- **[Concept implies a power level the archetype overshoots]** → optional `maxCr` gate returns the
  too-strong result with the roster so the LLM picks a weaker archetype.
- **[`CanonicalText` may be large]** → it's one stat block, bounded; acceptable as the grounded base.
- **[Persona invents stats anyway]** → the contract + the returned real numbers anchor it; same
  discipline as `recommend_build`/`ask_setting_lore` (compose from the grounded data, never fabricate).
- **[Thin over a plain entity lookup]** → the value is the archetype-validation + suggest-on-miss +
  the grounding contract that separates real mechanics from invented flavor; setting-aware/party
  richness is the deferred v2.
