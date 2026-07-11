## Context

The shipped `combat-initiative-tracker` drafts encounter monsters with auto-rolled initiative (from
the entity's Dex, via `MonsterRef.InitiativeModifier` + `MonsterDex.TryReadModifier`) but sets their
`MaxHp`/`CurrentHp` to 0 — the DM types HP by hand. The monster entity's `Fields` already carry
`hp: { average: int, formula: string?, special: string? }` (confirmed in `MonsterFields.schema.json`;
Goblin = average 7, formula `2d6`). Turn tracking is identity-based (`Combat.CurrentTurnCombatantId`,
from the 12b fix; the main spec still said `CurrentTurnIndex` and is corrected in this change's
delta). `RemoveCombatantAsync` deletes a row without touching the current-turn pointer, so removing
the acting combatant strands the marker. HP is adjusted ±1 per click via `AdjustHpAsync(c, delta)`.

## Goals / Non-Goals

**Goals:**

- Encounter-drafted monsters arrive combat-ready with real HP (average, or app-rolled).
- Apply arbitrary damage/heal in one action.
- Removing the current combatant leaves a sensible turn marker.

**Non-Goals:**

- No new table/migration, DI change, HTTP route, or MCP tool.
- No conditions-with-duration, manual reorder, or global scratch surface (separate later slices).
- Not simulating multi-die special HP (`hp.special`) — average/formula only.

## Decisions

**Monster HP mirrors the shipped monster-Dex path exactly.** `MonsterRef` gains `int AverageHp = 0`
and `string? HpFormula = null` (additive, defaulted — encounter tools/chat/math unaffected). A
`MonsterHp.TryRead(JsonElement fields, out int average, out string? formula)` helper (sibling of
`MonsterDex.TryReadModifier`, same file) reads `fields.hp.average`/`fields.hp.formula`; the three
`MonsterRef` construction sites (`EntitySearchMonsterSource.FindAsync`, the two in
`EncounterDesignService.ResolveMonsterAsync`) populate it. Keeping the same shape means the reader,
the sites, and the tests all parallel work already reviewed and shipped. *Alternative:* inject
`IEntityRetrievalService` into `CombatService` to look HP up at draft time — rejected; it re-adds the
Qdrant dependency the tracker deliberately avoids and duplicates the encounter layer's entity read.

**Roll-vs-average is a draft-time flag, defaulting to average.** `DraftMonstersAsync(..., bool
rollHp)`: `rollHp && HpFormula` parses via `DiceExpression.TryParse` (whitespace-stripped) → roll
through the injected `DiceRoller` → `MaxHp`; else `AverageHp`; else 0. `CurrentHp = MaxHp`.
Deterministic under a seeded `IRandomSource` (same test seam as init). The UI toggle (`_rollMonsterHp`)
lives on the tracker and is threaded through the existing `OnBuilt → AddMonstersAsync` flow. Average
is the default because it is what most tables use and it is deterministic; rolled is opt-in for
variety. *Alternative:* always roll — rejected; removes the deterministic default DMs expect.

**Damage/heal-by-N reuses `AdjustHpAsync`, no new repo method.** The row keeps a `−`/`+` pair but they
apply an N (a small per-row number field, default 1) instead of a hardcoded 1: `−` = `AdjustHpAsync(c,
-N)`, `+` = `AdjustHpAsync(c, +N)`. `AdjustHpAsync` already clamps to `0…MaxHp` and persists via
`UpdateCombatantAsync`. N=1 is behavior-identical to today, so the change is pure UI over tested logic.

**Remove-current re-anchors in the repository, by identity, using the shared order.** In
`RemoveCombatantAsync`, if the combatant being removed IS `CurrentTurnCombatantId`: load the remaining
combatants, `CombatantOrder.Sort` them, and set `CurrentTurnCombatantId` to the combatant that now
occupies the removed one's position (i.e. the next-in-order; wrap to the first if it was last; `null`
if none remain) — computed *before* the delete from the pre-removal order so "next" is well-defined.
`Round` is unchanged. Doing this in the repository (not the UI) keeps the invariant "the current-turn
pointer never references a removed combatant" enforced at the data layer, consistent with how
`AdvanceTurnAsync` already owns turn logic.

## Risks / Trade-offs

- **[HP formula won't parse]** (`2d6 + 2`, `18d10+90`, odd spacing) → strip whitespace before
  `DiceExpression.TryParse`; on failure fall back to `AverageHp`; a test covers the fallback.
- **[`DraftMonstersAsync` signature change ripples]** → one production caller (the tracker's
  `AddMonstersAsync`) and the drafting tests; update both. New `MonsterRef` fields are defaulted so no
  other `new MonsterRef(...)` site breaks.
- **[Remove-current re-anchor off-by-one]** → derive "next" from the pre-removal sorted order at the
  removed one's index (wrap; null when empty); cover with a real-Postgres test (remove current →
  marker is the next-in-order, not the top; remove last → null).
- **[UI clutter from a per-row N field]** → keep it compact (a narrow number input between the ± and
  the conditions); screenshot-review the row.

## Migration Plan

No schema/data migration. Steps: (1) extend `MonsterRef` + `MonsterHp.TryRead` + populate 3 sites;
(2) `DraftMonstersAsync` HP + `rollHp`; (3) `RemoveCombatantAsync` re-anchor; (4) tracker UI (roll-HP
toggle, damage/heal-by-N); (5) verify build/suite + screenshots. Rollback is a code revert — nothing
persisted changes shape.

## Verification

No new persisted shape, so verification is: `dotnet build` 0/0; full `dotnet test` green with new
real-Postgres tests (HP-average draft, rolled-formula draft over a seeded RNG, formula-fallback,
remove-current re-anchor, remove-last clears) + a `MonsterHp.TryRead` unit test; and Playwright
screenshots of the tracker showing the roll-HP toggle, damage/heal-by-N in action, and the
remove-current turn behavior.
