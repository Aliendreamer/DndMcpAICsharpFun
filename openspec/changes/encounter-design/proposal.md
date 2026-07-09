## Why

The companion can retrieve rules/entities and answer questions about the signed-in user's
character, but it can't yet *reason* — turn that data into decisions. Encounter design is the
first companion-reasoning surface (the north star): given a party and a target difficulty, help
a DM rate or build a balanced combat encounter. It has a crisp, testable rules basis (the DMG XP
budgets) and reuses what already exists — the campaign's heroes (levels) and the monster corpus in
`dnd_entities` (each with a CR) — so it delivers real reasoning value on a solid deterministic core.

## What Changes

- Add a pure, table-driven **`EncounterMath`** core covering **both editions** (edition selects the
  table and whether the multiplier applies):
  - **CR→XP** — the fixed 34-row table (CR 0…30), shared.
  - **2014** — per-character Easy/Medium/Hard/Deadly XP thresholds by level, summed over the party;
    an **encounter multiplier** by monster count (×1…×4), shifted one band up for <3 PCs and one
    band down for ≥6 PCs; difficulty = `sum(monsterXP) × multiplier` vs the thresholds.
  - **2024** — per-character Low/Moderate/High XP budget by level, summed; **no** multiplier;
    difficulty = raw `sum(monsterXP)` vs the budgets.
- Add **`EncounterAssessor`** (rate): party levels + a monster list + edition → total/adjusted XP,
  the difficulty band, and the surrounding thresholds for context ("Hard; Deadly starts at 3,600").
- Add **`EncounterGenerator`** (build): party + target difficulty + edition + optional theme/CR
  constraints → retrieve candidate monsters via the **existing entity search** (`type=Monster`,
  `crNumeric_lte/gte`, `keyword`, `srd`, edition), greedily select a set that lands in the target
  band (accounting for the 2014 count→multiplier feedback loop), and return the monsters **plus the
  Assessor's verdict** — so a built encounter is rated by the same math and can never disagree with
  a directly-rated one.
- Add **`EncounterDesignService`** and **two per-user MCP chat tools** (`rate_encounter`,
  `build_encounter`) following the existing `resolve_character_feature` pattern: SEC-08 in-process,
  closing over the authenticated session user id (never a spoofable argument). Party source =
  the campaign's heroes' levels by default (ownership-checked), with an explicit `partyLevels`
  override for hypotheticals.
- No new HTTP route (the tools are in-process MCP tools like the character tools) → no
  `.http`/`.insomnia` changes.

## Capabilities

### New Capabilities

- `encounter-design`: the `EncounterMath` core (both editions), the Assessor (rate), the Generator
  (build, sharing the Assessor's math), the `EncounterDesignService`, and the two per-user
  `rate_encounter` / `build_encounter` chat tools with campaign-heroes party resolution.

### Modified Capabilities

- (none — the two tools are new per-user chat tools; no existing spec's requirements change.)

## Impact

- **Code:** new `Features/Encounters/` — `EncounterMath`, `EncounterAssessor`, `EncounterGenerator`,
  `EncounterDesignService`, and DTOs; new per-user tools wired into `DndChatService` (SEC-08 in the
  `if (long.TryParse(idClaim, out userId))` block, routing through a `*ForUser` ownership chain);
  monster-CR resolution + candidate retrieval reuse the existing entity search/`get_entity`.
- **APIs:** two new MCP chat tools (per-user, in-process); no new HTTP route, no auth-surface change.
- **Data:** read-only — party levels from the user's campaign heroes, monsters from `dnd_entities`.
  No schema change (edition is a tool parameter, not stored on the campaign).
- **Docs:** none (`.http`/`.insomnia` unchanged — the tool descriptions are the surface).
