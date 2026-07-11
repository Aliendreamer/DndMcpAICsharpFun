## Context

`CombatantOrder.Sort` orders combatants `InitiativeRoll desc → InitiativeModifier desc → IsPlayer
(player first) → AddedOrder asc → Id`. `AddedOrder` is a stored `int` on `Combatant`, set to the
combatant count at add time — the stable insertion tie-break. The DM can edit `InitiativeRoll` but
cannot control the order among combatants the sort can't otherwise distinguish. The current turn is
`CurrentTurnCombatantId` (identity-based, from the shipped tracker).

## Goals / Non-Goals

**Goals:** let the DM settle initiative ties (▲/▼); never produce an initiative-inconsistent order;
leave the current turn untouched.

**Non-Goals:** no full manual override (can't move a 10 above a 15); no new table/migration/DI/HTTP/MCP;
no drag-and-drop (▲/▼ is enough and keyboard-accessible).

## Decisions

**Reuse `AddedOrder` as the mutable tie-break — no new field.** The sort already uses `AddedOrder` as
the last discriminator, so swapping two combatants' `AddedOrder` flips their order exactly when every
higher key is equal. No schema change. *Alternative:* a dedicated `ManualOrder` column — rejected;
`AddedOrder` already serves this role and adding a column needs a migration for no benefit.

**A tie is "equal on all keys above `AddedOrder`."** Add `CombatantOrder.AreTied(a, b)` — true when
`a.InitiativeRoll == b.InitiativeRoll` (both null counts as equal), `a.InitiativeModifier ==
b.InitiativeModifier`, and `a.IsPlayer == b.IsPlayer`. This is exactly the condition under which
`AddedOrder` decides their order, so it's the exact condition under which a swap has an effect. Using
one shared helper for both the repo's guard and the UI's enable/disable keeps them consistent.
*Alternative:* gate on `InitiativeRoll` alone — rejected; two combatants with equal rolls but
different modifiers are already ordered by modifier, so swapping `AddedOrder` would be a confusing
no-op the UI would wrongly enable.

**Reorder is a repository swap, ownership-scoped, atomic.** `MoveCombatantAsync(combatantId, combatId,
campaignId, userId, bool up)`: ownership-check; load the combatants; `CombatantOrder.Sort`; find the
target's index and the directional neighbor (`up` = index−1, `down` = index+1); if that neighbor
exists AND `AreTied(target, neighbor)`, swap their `AddedOrder` on the tracked entities and one
`SaveChangesAsync` (atomic); otherwise no-op. The guard means a foreign user, a missing combatant, an
edge (top/bottom), or a non-tied neighbor all change nothing. *Alternative:* the UI computes the two
ids and calls a generic swap — rejected; centralizing the tie-guard + direction resolution in the repo
keeps the invariant ("only tied combatants swap") enforced at the data layer, matching how
`AdvanceTurnAsync`/`RemoveCombatantAsync` own their ordering logic.

**Current turn is inherently safe.** The reorder only mutates `AddedOrder`; `CurrentTurnCombatantId`
references an identity, so the acting combatant stays acting regardless of its tie-break position. No
extra handling needed — asserted by a test.

**UI: ▲/▼ per row, enabled by `AreTied` against the display neighbor.** The tracker already renders the
sorted list; for each position it checks whether the previous/next combatant `AreTied` and enables ▲/▼
accordingly (disabled/inert otherwise). Clicking calls `MoveCombatantAsync(up)` then `ReloadAsync`.

## Risks / Trade-offs

- **[Swapping `AddedOrder` where higher keys differ would be a silent no-op or corrupt ordering]** →
  the repo guards with `AreTied` (only swaps genuine ties); the UI disables the arrow with the same
  helper, so a swap is never issued where it wouldn't reorder.
- **[Atomicity of the two-row swap]** → both combatants are loaded tracked and swapped in one
  `SaveChangesAsync` (single atomic write; no second write, so no execution-strategy transaction
  needed — unlike `RemoveCombatantAsync`'s two writes).
- **[Reorder vs current-turn]** → identity-based turn pointer is unaffected by an `AddedOrder` swap;
  covered by a test.
- **[Duplicate/непоследовательные `AddedOrder` values after many swaps]** → `AddedOrder` need not be
  contiguous or unique; the sort only needs a relative order, and a swap preserves the multiset of
  values, so no drift.

## Migration Plan

No schema/data migration. Steps: (1) `CombatantOrder.AreTied`; (2) `MoveCombatantAsync`; (3) UI ▲/▼;
(4) verify. Rollback is a code revert — no persisted shape changes.

## Verification

Build 0/0; full `dotnet test` green with new real-Postgres tests (tie swap flips order; non-tie move
is a no-op; foreign-user move changes nothing; current-turn identity preserved across a reorder) + a
`CombatantOrder.AreTied` unit test; and a Playwright screenshot of ▲/▼ reordering a tie.
