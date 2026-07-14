## Why

The shipped `generate_npc` grounds a SINGLE NPC to a real stat block, but a DM prepping a scene usually
needs a whole **cast** — a leader plus supporting figures. Doing that today means the LLM calling
`generate_npc` several times in sequence, which qwen3 does unreliably. The obvious "party" shapes (a
list-of-archetypes param, or fattening the already-qwen3-flaky 4-param `prep_session`) all lean on
exactly the multi-param/array tool-calling qwen3 can't handle. This adds a **single-string-param** tool
that returns a themed ensemble of grounded NPCs in one reliable call.

## What Changes

- **`NpcPartyTemplates`** — a deterministic keyword→ensemble table. Each template is an ordered list of
  `(Role, Archetype)` drawn from `NpcArchetypes.Common` (criminal → Bandit Captain + Thug×2 + Spy;
  military → Veteran + Guard×2 + Scout; cult → Cult Fanatic + Cultist×2 + Acolyte; noble → Noble +
  Guard×2 + Spy; arcane → Mage + Acolyte + Guard×2; default → Veteran + Guard×2 + Commoner). The theme
  is matched case-insensitively (first hit wins; no match → default). **Theme selects a template and is
  handed to the LLM for flavor — it is NEVER a monster-search filter** (encoding the session-prep
  theme-over-filter lesson structurally).
- **`NpcGenerationService.GeneratePartyAsync(theme, ct)`** — picks the template, grounds each member by
  archetype NAME through the existing anti-fuzzy resolve (real Monster stat block per member,
  per-member not-in-corpus flag), returns a `GeneratedNpcParty`.
- **`generate_npc_party(theme)` chat tool** — a single required string param, ownership-free (like
  `generate_npc`). Returns the ensemble; the LLM names/flavors each member to the theme, takes all stats
  from the returned blocks and cites them, never invents stats.

## Capabilities

### New Capabilities

- `npc-party-generation`: `NpcPartyTemplates`, `GeneratePartyAsync`, and the ownership-free
  `generate_npc_party` chat tool.

### Modified Capabilities

<!-- None. generate_npc (single NPC) is unchanged; this adds a sibling party tool. -->

## Impact

- **Code:** new `Features/Npc/NpcPartyTemplates.cs` + result records; extend
  `Features/Npc/NpcGenerationService.cs` with `GeneratePartyAsync` (reuses the existing per-member
  grounding + `IEntityRetrievalService`). `Features/Chat/DndChatService.cs` — register
  `generate_npc_party` in the authenticated block.
- **No** new retrieval service, migration, HTTP route, `.http`/`.insomnia` change, or ownership
  coupling. qwen3-reliability is a first-class design constraint: exactly one string param.
