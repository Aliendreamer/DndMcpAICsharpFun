## Why

When two combatants roll the same initiative, the tracker breaks the tie deterministically (by
insertion order) but the DM can't say who actually goes first. This lets the DM reorder tied
combatants with ▲/▼ — without letting them produce a nonsensical order (a 10 sitting above a 15).

## What Changes

- **Manual tie reorder** — ▲/▼ on a combatant reorders it among others the sort treats as tied (equal
  initiative roll, modifier, and side). It swaps the two combatants' tie-break order; the arrows are
  inert when the combatant has no true-tie neighbor in that direction, so initiative still drives the
  overall order and a reorder can never cross a real initiative difference.
- The current turn is untouched (it's tracked by identity, and a reorder only swaps the tie-break key).

No new table or migration (reuses the existing `AddedOrder` column as the swap key), no HTTP route, no
MCP tool — so no `.http` / `.insomnia` change.

## Capabilities

### New Capabilities

<!-- None — this modifies the existing combat-initiative-tracker capability. -->

### Modified Capabilities

- `combat-initiative-tracker`: the **turn-advancement/ordering** requirement now also lets the DM
  manually reorder combatants the sort treats as tied (equal `InitiativeRoll`, `InitiativeModifier`,
  and side) by swapping their tie-break order; the reorder never changes the relative order of
  combatants the sort can already distinguish, and never changes the current turn.

## Impact

- **Modified code**: `Features/Combat/CombatantOrder.cs` (a pure `AreTied(a, b)` helper),
  `Features/Combat/CombatRepository.cs` (a `MoveCombatantAsync(..., bool up)` ownership-scoped swap of
  two tied combatants' `AddedOrder`), `CompanionUI/Components/InitiativeTracker.razor` (▲/▼ per row,
  enabled only against a true-tie neighbor).
- **No** new table/migration, DI, or HTTP/MCP change; `AddedOrder` is reused as the mutable tie-break.
- **Verification**: build 0/0, full `dotnet test` green (real-Postgres tests for the tie swap,
  non-tie no-op, ownership, and current-turn preservation; a `CombatantOrder.AreTied` unit test), and
  a Playwright screenshot of ▲/▼ reordering a tie.
