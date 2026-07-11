## 1. ConditionTimer model + backward-compatible (de)serialization

- [ ] 1.1 Add `sealed record ConditionTimer(Condition Condition, int? RoundsRemaining = null)` in `Features/Combat/` (e.g. in `Combatant.cs` next to `CombatantConditions`)
- [ ] 1.2 Retype the `[NotMapped] Combatant.Conditions` helper to `IReadOnlyList<ConditionTimer>` (get = deserialize, set = serialize into `ConditionsJson`)
- [ ] 1.3 Update `CombatantConditions.Serialize` to write the new object shape (`[{Condition, RoundsRemaining}]`, enum names as strings); update `Deserialize` to read BOTH shapes — old array-of-strings → `ConditionTimer` with null count, new array-of-objects → directly (branch on the first element's `JsonValueKind`: `String` vs `Object`)
- [ ] 1.4 Unit test (`CombatantConditionsTests` or a new file): new object-array round-trips (timed + indefinite mixed); OLD string-array (`["Poisoned","Prone"]`) deserializes to two indefinite timers; empty/`"[]"` → empty

## 2. Repository: retype UpdateCombatantAsync + tick on round rollover

- [ ] 2.1 Change `CombatRepository.UpdateCombatantAsync`'s conditions parameter from `IReadOnlyList<Condition>` to `IReadOnlyList<ConditionTimer>` (persist via `CombatantConditions.Serialize`); confirm the only caller is the UI (Task 3 updates it)
- [ ] 2.2 In `AdvanceTurnAsync`, load the combatants TRACKED; on the round-rollover branch (where `Round += 1`), for each combatant decrement every timed condition's `RoundsRemaining` by 1 and drop any `<= 0`, leaving indefinite (null) conditions unchanged, and write it back to `ConditionsJson` — all within the existing single `SaveChangesAsync` (one atomic write; no transaction needed)
- [ ] 2.3 Real-Postgres tests (extend `CombatRepositoryCombatantTests`): a timed condition set on a combatant persists with its count; advancing across a round rollover decrements timed conditions and removes any reaching 0 while indefinite ones are untouched; the tick applies to MULTIPLE combatants at once; advancing WITHIN a round (no rollover) does NOT change any rounds-remaining

## 3. InitiativeTracker UI: per-chip rounds field

- [ ] 3.1 Update the condition rendering: each ACTIVE condition chip shows its name + a small number input bound to that condition's `RoundsRemaining` (empty = indefinite/∞, a positive int = timed → chip reads e.g. "poisoned (3)"); the "+" popover still adds a condition indefinite by default
- [ ] 3.2 Wire edits: toggling a condition on/off and editing a chip's rounds both build the updated `IReadOnlyList<ConditionTimer>` and call `UpdateCombatantAsync` (retyped); clearing the number → indefinite; typing a number → timed; remove-chip works as today
- [ ] 3.3 Style `.cond-rounds` (narrow mono field on the chip) against the design system; keep chips compact/no overflow

## 4. Verify

- [ ] 4.1 `dotnet build` 0/0; full `dotnet test` suite green (incl. Tasks 1–2 tests); confirm NO `.http`/`.insomnia`/`Migrations/`/schema change; confirm the `Conditions`/`UpdateCombatantAsync` retype broke no other caller
- [ ] 4.2 Rebuild the dev container; Playwright-screenshot: set a combatant's condition to a duration (e.g. "poisoned (2)"), advance the turn across a round rollover, watch it tick to (1) then expire; an indefinite condition on another combatant stays; desktop + mobile, no overflow
- [ ] 4.3 Final whole-branch review (opus): backward-compat deser (both shapes); tick/expire off-by-one + indefinite-never-ticks + all-combatants + within-round-no-tick; the AdvanceTurn mutation stays atomic in one SaveChanges; retype ripple contained; MODIFIED deltas match shipped behavior; no scope creep
