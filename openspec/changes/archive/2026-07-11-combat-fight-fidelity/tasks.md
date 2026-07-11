## 1. Monster HP on MonsterRef

- [ ] 1.1 Extend `MonsterRef` (`Features/Encounters/EncounterAssessment.cs`) with `int AverageHp = 0, string? HpFormula = null` (defaulted last params — existing `new MonsterRef(...)` sites still compile)
- [ ] 1.2 Add `MonsterHp.TryRead(JsonElement fields, out int average, out string? formula)` in `Features/Encounters/EntitySearchMonsterSource.cs` (sibling of `MonsterDex.TryReadModifier`): reads `fields.hp.average` (int) + `fields.hp.formula` (string?); returns false when `hp`/`average` absent
- [ ] 1.3 Unit test `MonsterHp.TryRead`: average+formula from a fields JSON (`{"hp":{"average":7,"formula":"2d6"}}` → 7,"2d6"); missing `hp` → false/0/null
- [ ] 1.4 Populate HP at all 3 `MonsterRef` construction sites — `EntitySearchMonsterSource.FindAsync` + the two in `EncounterDesignService.ResolveMonsterAsync` (mirror the existing `MonsterDex` calls; default to 0/null on miss)

## 2. CombatService.DraftMonstersAsync — set monster HP

- [ ] 2.1 Add a `rollHp` parameter to `DraftMonstersAsync(long combatId, long campaignId, long userId, IReadOnlyList<MonsterRef> monsters, bool rollHp)`
- [ ] 2.2 Per monster compute `MaxHp`: `rollHp && HpFormula` parses via `DiceExpression.TryParse` (whitespace-stripped) → roll through the injected `DiceRoller` (`.Total`) → MaxHp; else `AverageHp`; if 0/none → 0. Set `CurrentHp = MaxHp` (replace the current hardcoded `MaxHp = 0, CurrentHp = 0`)
- [ ] 2.3 Real-Postgres tests (extend `CombatServiceDraftingTests`, seeded `IRandomSource`): `rollHp=false` → MaxHp==AverageHp; `rollHp=true` with a formula → MaxHp==deterministically-rolled total (CurrentHp==MaxHp); `rollHp=true` with average-but-no-formula → MaxHp==AverageHp (fallback)

## 3. RemoveCombatantAsync — re-anchor the current turn

- [ ] 3.1 In `CombatRepository.RemoveCombatantAsync`, when the removed combatant IS `combat.CurrentTurnCombatantId`: from the PRE-removal `CombatantOrder.Sort` order, compute the next-in-order (the combatant after the removed one; wrap to first if it was last; `null` if it was the only one), then set `CurrentTurnCombatantId` to that id as part of the same operation. `Round` unchanged. (Non-current removals leave the pointer untouched.)
- [ ] 3.2 Real-Postgres tests (extend `CombatRepositoryCombatantTests`): removing the CURRENT combatant sets `CurrentTurnCombatantId` to the next-in-order (not the top) and does NOT change `Round`; removing the last remaining combatant sets `CurrentTurnCombatantId` to null; removing a NON-current combatant leaves `CurrentTurnCombatantId` unchanged

## 4. InitiativeTracker UI

- [ ] 4.1 Roll-HP toggle: add a `_rollMonsterHp` view-state field + a "🎲 Roll monster HP" checkbox in the tracker add area; `AddMonstersAsync` passes `_rollMonsterHp` into `DraftMonstersAsync` (Serena edit; existing OnBuilt→AddMonstersAsync flow otherwise unchanged)
- [ ] 4.2 Damage/heal-by-N: give each combatant row a compact N number field (per-row view-state, default 1) between the ± and the conditions; `−` calls `AdjustHpAsync(c, -N)`, `+` calls `AdjustHpAsync(c, +N)` (reuse the existing handler; keep it clamped 0..MaxHp). N=1 behaves as before
- [ ] 4.3 Style the new controls against the design system (`.chip`/`.btn`/token inputs); keep the row compact

## 5. Verify

- [ ] 5.1 `dotnet build` 0/0; full `dotnet test` suite green (incl. the new HP-draft + remove-current tests); confirm NO `.http`/`.insomnia`/schema/migration change; confirm no other `new MonsterRef(...)` site broke
- [ ] 5.2 Rebuild the dev container; Playwright-screenshot the tracker: build an encounter with roll-HP OFF (monsters show average HP) and ON (rolled), damage-by-N on a combatant, and remove the current combatant (turn moves to the next-in-order); desktop + mobile
- [ ] 5.3 Final whole-branch review (opus): drafting sets HP correctly + fallback; remove-current re-anchor by identity (no off-by-one, `Round` untouched); `DraftMonstersAsync` signature ripple handled; no scope creep; MODIFIED deltas match the shipped behavior
