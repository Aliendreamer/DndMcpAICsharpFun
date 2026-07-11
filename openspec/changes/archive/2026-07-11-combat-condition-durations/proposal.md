## Why

The tracker marks conditions but doesn't time them — a DM who says "poisoned for 2 rounds" or
"paralyzed for 3" has to remember the count and clear it by hand. This adds an optional **rounds
remaining** to each condition so the tracker counts them down and drops them automatically, with each
combatant's each condition timed independently.

## What Changes

- **Condition durations** — each active condition on a combatant optionally carries a rounds-remaining
  count (unset = indefinite, i.e. today's behavior). Durations are per-condition *and* per-combatant:
  Aria can be Paralyzed (3) while a Goblin is Poisoned (2), each on its own separate counter.
- **Auto-tick on round rollover** — when the combat advances into a new round, every combatant's
  *timed* conditions drop by 1 and any that reach 0 expire (are removed). Indefinite conditions never
  tick. The tick applies to all combatants at once (once per round).
- **UI** — the condition-add flow is unchanged (pick from the "+" popover, added indefinite by
  default). Each active condition chip gains a small rounds field: empty = ∞, a number = timed (the
  chip reads "poisoned (3)" and visibly counts down each round). Clearing the number makes it
  indefinite again.

No new table or migration (the per-combatant conditions JSON changes shape, read back-compatibly), no
HTTP route, no MCP tool — so no `.http` / `.insomnia` change.

## Capabilities

### New Capabilities

<!-- None — this modifies the existing combat-initiative-tracker capability. -->

### Modified Capabilities

- `combat-initiative-tracker`: the **conditions** requirement now lets each condition carry an optional
  rounds-remaining (indefinite when unset); the **turn-advancement** requirement now decrements every
  combatant's timed conditions by 1 on a round rollover and expires them at 0.

## Impact

- **Modified code**: `Features/Combat/Combatant.cs` (a `ConditionTimer(Condition, int? RoundsRemaining)`
  record; the `Conditions` helper + `CombatantConditions` serialize/deserialize carry timers, with a
  backward-compatible read of the old string-array shape), `Features/Combat/CombatRepository.cs`
  (`UpdateCombatantAsync` conditions param → timers; `AdvanceTurnAsync` ticks/expires on round
  rollover), `CompanionUI/Components/InitiativeTracker.razor` (per-chip rounds field + timer-aware
  toggle/display).
- **No** new table/migration, DI, or HTTP/MCP change. The `ConditionsJson` column is reused; existing
  rows (old string-array shape) read as indefinite timers.
- **Verification**: build 0/0, full `dotnet test` green (real-Postgres tests for duration round-trip,
  round-rollover tick/expire, indefinite-never-ticks, all-combatants; a backward-compat deserialization
  unit test), and a Playwright screenshot of a timed condition ticking down and expiring.
