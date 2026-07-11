## Why

The initiative tracker runs a fight, but three friction points slow a real table: encounter-drafted
monsters arrive at **0 HP** (the DM types every monster's hit points by hand), applying damage is
**one click per point** (11 damage = eleven clicks), and **removing the current combatant** (a
monster dies on its turn) leaves the turn marker stranded — the next advance jumps back to the top
instead of the next creature in order. This slice makes the tracker fight-ready.

## What Changes

- **Monster auto-HP** — a built encounter's monsters drop into the tracker with real `MaxHp` from
  the stat block: the book **average** by default, or **app-rolled** from the HP formula (e.g. `2d6`)
  when a "🎲 Roll monster HP" toggle is on. The entity already carries both `hp.average` and
  `hp.formula`; this reads them the same way monster initiative already reads Dex.
- **Damage / heal by N** — the combatant HP controls apply an amount: a small N field (default 1)
  with − / + that subtract/add N at once (clamped 0…Max). N=1 behaves exactly like today.
- **Remove-current turn fix** — when the combatant whose turn it is gets removed, the current turn
  re-points to the **next combatant in initiative order** (wrapping if it was last; cleared if it
  was the last one standing) instead of leaving a stale/absent marker.

No new tables, no migration, no HTTP route or MCP tool (drafting is a Blazor-side call) — so no
`.http` / `.insomnia` change.

## Capabilities

### New Capabilities

<!-- None — this slice modifies the existing combat-initiative-tracker capability. -->

### Modified Capabilities

- `combat-initiative-tracker`: the **combatant-drafting** requirement now also sets a monster's
  `MaxHp`/`CurrentHp` (book average, or app-rolled from the formula via a roll toggle); the
  **turn-advancement** requirement is corrected to the shipped identity-based current-turn tracking
  and now re-anchors the current turn to the next-in-order when the current combatant is removed.

## Impact

- **Modified code**: `Features/Encounters/EncounterAssessment.cs` (`MonsterRef` +`AverageHp`/`HpFormula`),
  `Features/Encounters/EntitySearchMonsterSource.cs` (a `MonsterHp.TryRead` helper + populate at the
  build site), `Features/Encounters/EncounterDesignService.cs` (populate at the two resolve sites),
  `Features/Combat/CombatService.cs` (`DraftMonstersAsync` gains `rollHp` + sets HP),
  `Features/Combat/CombatRepository.cs` (`RemoveCombatantAsync` re-anchors the current turn),
  `CompanionUI/Components/InitiativeTracker.razor` (roll-HP toggle, damage/heal-by-N controls).
- **No** schema/migration, DI, or domain-model-table change. **No** `.http`/`.insomnia` change.
  `MonsterRef`'s new fields are additive/defaulted, so the encounter tools/chat and `encounter-design`
  math are unaffected (no delta there).
- **Verification**: build 0/0, full `dotnet test` suite green (real-Postgres tests for HP drafting +
  remove-current re-anchor + a `MonsterHp` unit test), and Playwright screenshots of the tracker
  (roll-HP toggle, damage/heal-by-N, remove-current turn).
