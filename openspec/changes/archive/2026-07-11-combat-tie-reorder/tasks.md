## 1. CombatantOrder.AreTied helper

- [ ] 1.1 Add `public static bool AreTied(Combatant a, Combatant b)` to `Features/Combat/CombatantOrder.cs` — true when `a.InitiativeRoll == b.InitiativeRoll` (both null = equal), `a.InitiativeModifier == b.InitiativeModifier`, and `a.IsPlayer == b.IsPlayer` (the exact keys above `AddedOrder`)
- [ ] 1.2 Unit test (`CombatantOrderTests`): equal keys → true (incl. both-null `InitiativeRoll`); differing `InitiativeRoll` / `InitiativeModifier` / `IsPlayer` → false

## 2. Repository: MoveCombatantAsync (tie swap)

- [ ] 2.1 Add `CombatRepository.MoveCombatantAsync(long combatantId, long combatId, long campaignId, long userId, bool up)`: ownership-check (campaign+user); load combatants; `CombatantOrder.Sort`; find the target's index; neighbor = `up ? index-1 : index+1`; if the neighbor is in range AND `CombatantOrder.AreTied(target, neighbor)`, swap their `AddedOrder` on the tracked entities and `SaveChangesAsync` (one atomic write); else no-op
- [ ] 2.2 Real-Postgres tests (extend `CombatRepositoryCombatantTests`): two combatants tied on the same `InitiativeRoll`/modifier/side — `MoveCombatantAsync(up)` on the lower one swaps their sorted order; a move against a NON-tie neighbor (different `InitiativeRoll`) leaves the order unchanged; a foreign user's move changes nothing; advancing to set a `CurrentTurnCombatantId` then reordering leaves that current-turn id unchanged; moving at the top/bottom edge is a no-op

## 3. InitiativeTracker UI: ▲/▼ per row

- [ ] 3.1 In the combatant row, add ▲/▼ buttons; from the already-sorted display list, enable ▲ only when the previous combatant `CombatantOrder.AreTied` with this one, and ▼ only when the next does (disabled/inert otherwise); clicking calls `MoveCombatantAsync(c.Id, _combat.Id, CampaignId, UserId, up: true/false)` then `ReloadAsync`
- [ ] 3.2 Style the ▲/▼ controls (compact, token colors, clear disabled state) against the design system; keep the row from overflowing

## 4. Verify

- [ ] 4.1 `dotnet build` 0/0; full `dotnet test` suite green (incl. Tasks 1–2 tests); confirm NO `.http`/`.insomnia`/`Migrations/`/schema change
- [ ] 4.2 Rebuild the dev container; Playwright-screenshot: two combatants tied on the same initiative — use ▲/▼ to swap their order; confirm the ▲/▼ are inert on a unique-initiative combatant, and the current-turn highlight stays on the same combatant across a reorder; desktop + mobile, no overflow
- [ ] 4.3 Final whole-branch review (opus): `AreTied` matches the sort's above-`AddedOrder` keys; `MoveCombatantAsync` swaps only genuine ties, ownership-scoped, atomic, current-turn preserved, edge/no-tie no-ops; UI enable/disable uses the same helper; MODIFIED delta preserves all prior sort/advance/round-tick/remove-current content; no scope creep
