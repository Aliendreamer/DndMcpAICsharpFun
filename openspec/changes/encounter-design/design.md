## Context

The companion (`DndChatService`) exposes retrieval tools (`search_lore`, `search_entities`,
`get_entity`, `search_dnd`) plus per-user character tools (`resolve_character_feature`,
`check_multiclass`) that close over the authenticated session user id (SEC-08) and route through an
ownership chain (`GetSnapshotForUserAsync`-style: throw Unauthorized when the resource isn't the
caller's). Campaigns own heroes; each `HeroSnapshot` carries a `Level` and a `CharacterSheet`
(`Classes`, derived total `Level`). Monsters live in `dnd_entities` with a `cr` field (indexed as
`crNumeric`); the entity search already supports `type=Monster`, `crNumeric_lte/gte`, `keyword`,
`srd`, and edition filters. There is no edition field on `Campaign`/`Hero`.

The two D&D editions build encounters with genuinely different math, and both are well-defined
lookup tables — so this is a deterministic-core problem, not a judgment problem, up to the point of
monster *selection*.

## Goals / Non-Goals

**Goals:**

- A pure, table-driven encounter-math core that is correct for both editions and testable without
  any I/O.
- One shared difficulty definition used by BOTH rate and build, so a built encounter can never be
  rated differently than the same monsters passed to rate.
- Reuse the existing monster corpus + entity search for candidate selection; reuse the existing
  per-user tool + ownership pattern for the chat surface.

**Non-Goals:**

- Optimal monster packing / exhaustive encounter search — greedy "lands in the target band,
  thematically plausible" is enough.
- Storing edition on the campaign (edition is a tool parameter; no schema change).
- Non-combat encounters, environmental hazards, or treasure/XP-reward budgeting.
- A new HTTP endpoint — the surface is the in-process MCP chat tools, like the character tools.

## Decisions

### One math core, two editions behind an edition parameter

`EncounterMath` is pure and table-driven. `Cr→Xp` is the fixed 34-row table shared by both editions.
The edition parameter selects: (a) the per-level party table (2014 Easy/Medium/Hard/Deadly
thresholds vs 2024 Low/Moderate/High budgets) and (b) whether the count-based **multiplier** applies
(2014 only). Difficulty classification is then a band comparison. *Alternative considered:* two
separate math classes per edition — rejected; the shared CR→XP + a common "sum party table, classify
by band" shape is cleaner and keeps the editions from drifting on shared pieces.

### The Generator reuses the Assessor — single source of truth for difficulty

`EncounterGenerator` never re-implements difficulty. It computes the target budget from
`EncounterMath`, selects monsters, then calls `EncounterAssessor` on the chosen set and returns the
Assessor's verdict alongside the monsters. So `build_encounter(Hard)` → the returned difficulty is
whatever `rate_encounter` would say about those same monsters. *Alternative considered:* the
generator tracking difficulty itself while packing — rejected; that is the classic two-implementations
drift bug (a built "Hard" that rates "Deadly").

### 2014 count→multiplier feedback loop

Because the 2014 multiplier grows with monster count, adding a monster can push an in-budget set out
of band. The generator treats the assessed band (not raw XP) as the target: it grows/prunes the set
and re-assesses via the Assessor until the assessed band matches the requested difficulty (or it
exhausts candidates and returns the closest set it found, flagged). 2024 has no multiplier, so it is
a straight budget fill.

### Monster selection via the existing entity search

The generator queries `dnd_entities` through the existing entity-search path with `type=Monster`,
a `crNumeric` band derived from the per-monster share of the target budget, the optional `keyword`
(theme), `srd`, and the edition. It does not add a new retrieval mechanism. CR→XP maps each
candidate's `cr` to XP for the math. `rate_encounter`'s monster inputs (id or name) resolve to CR via
the existing `get_entity`/entity lookup.

### Per-user tools, ownership-checked, party from campaign heroes

Two tools are added inside `DndChatService`'s authenticated (`long.TryParse(idClaim, out userId)`)
block, closing over `userId` — never taking a user/campaign id as a trusted argument across the
shared-key boundary. `EncounterDesignService` exposes `*ForUserAsync` methods that resolve the party
from the campaign's heroes only after an ownership check (same pattern as the character tools; a
negative test proves caller A can't design for caller B's campaign). `partyLevels` is an explicit
override for hypothetical parties; if neither `campaignId` nor `partyLevels` is supplied, the tool
returns a clear error.

## Risks / Trade-offs

- **Sparse monster corpus for a theme/CR band** → the generator can't fill the budget. *Mitigation:*
  return the closest in-band set it can assemble with an explicit "couldn't fully fill / no thematic
  matches" note, never a silent under-budget encounter presented as on-target.
- **2014 multiplier loop non-termination / thrash** → bound the search (cap candidate count and
  iterations); on exhaustion return the closest assessed set, flagged.
- **Edition mismatch between party and monsters** → the edition parameter drives both the math and
  the monster filter, so they stay consistent; the tool documents that the caller picks the edition.
- **CR→XP / DMG table transcription errors** → the whole core's correctness rests on these tables;
  they are spot-checked against known DMG values at multiple levels and at classification boundaries
  in `EncounterMathTests`.
