## Context

Conditions are stored today as `Combatant.ConditionsJson` — a JSON array of `Condition` enum names,
exposed via the `[NotMapped] Conditions` helper (`IReadOnlyList<Condition>`) and the
`CombatantConditions.Serialize/Deserialize` pair (a `JsonStringEnumConverter`). The UI toggles them
in `InitiativeTracker.razor` (a "+" popover over the 15 conditions) and persists via
`CombatRepository.UpdateCombatantAsync(..., IReadOnlyList<Condition> conditions)`. `AdvanceTurnAsync`
moves `CurrentTurnCombatantId` to the next combatant and bumps `Round` on wrap, in a single
`SaveChangesAsync` on the tracked combat. There is no per-condition duration.

## Goals / Non-Goals

**Goals:**

- Optional per-condition, per-combatant rounds-remaining; indefinite when unset.
- Auto-decrement + expire on round rollover; indefinite conditions never tick.
- Existing (durationless) records read back unbroken.

**Non-Goals:**

- No new table/migration, DI, HTTP route, or MCP tool.
- Not modelling *when within a turn* a condition ends (per-turn ticking, save-ends) — this is a
  per-round countdown aid, not a rules engine.
- Not surfacing durations in the end-combat log breadcrumb (it carries no conditions).

## Decisions

**A `ConditionTimer(Condition Condition, int? RoundsRemaining)` list in the same JSON column.** The
per-combatant list becomes `IReadOnlyList<ConditionTimer>` (null count = indefinite), serialized into
the existing `ConditionsJson` column — a shape change, not a schema change, so **no migration**. Enum
names stay strings. *Alternative:* a separate `CombatantCondition` table — rejected; conditions are a
small bounded set owned wholly by the combatant, already modelled as an embedded JSON list, and a
table adds a migration + join for no query benefit.

**Backward-compatible deserialization.** `CombatantConditions.Deserialize` must read both shapes: the
old array-of-strings (`["Poisoned","Prone"]`) → timers with null count; the new array-of-objects
(`[{"Condition":"Poisoned","RoundsRemaining":2}, …]`) → directly. Detect by the first token / element
kind (string vs object) and map accordingly. This keeps in-progress combats working across the
deploy. *Alternative:* a migration to rewrite existing rows — rejected; unnecessary for an embedded
JSON list with a cheap read-time shim, and combats are short-lived anyway.

**Tick on the round rollover, inside AdvanceTurn's existing SaveChanges.** The decrement/expire happens
exactly where `Round += 1` already fires (the wrap case). `AdvanceTurnAsync` loads the combatants
*tracked* (it currently loads them untracked only to sort), and on a wrap mutates each combatant's
`ConditionsJson` (decrement timed entries, drop those hitting 0) before the single `SaveChangesAsync`
that also persists the round/turn move. One `SaveChanges` = one atomic write; no execution-strategy
transaction is needed (unlike `RemoveCombatantAsync`, which had two separate writes). *Alternative:* a
separate tick method the UI calls — rejected; it splits the invariant "a new round ticks conditions"
across two calls the UI must remember to pair.

**Per-round, all-combatants.** The tick applies to every combatant at once when the round rolls over —
matching the "3 rounds = 3 rounds" mental model the DM chose, and localizing the change to one branch.

**UI: per-chip rounds field.** Adding a condition (the "+" popover) still adds it indefinite. Each
active chip renders its name plus a small number input bound to that condition's rounds-remaining:
empty ⇒ indefinite, a positive integer ⇒ timed (chip shows "(N)"). Editing the field calls the
existing `UpdateCombatantAsync` (now timer-typed) with the updated list. Timed chips visibly count down
as rounds advance; a chip reaching 0 disappears on the next reload after the tick.

## Risks / Trade-offs

- **[Old/new JSON shape confusion]** → the deserializer branches on element kind (string vs object)
  and a unit test covers both; the serializer always writes the new object shape.
- **[Type ripple from `Conditions`/`UpdateCombatantAsync` retype]** → contained: the helper, the repo
  method, and the razor toggle/display are the only touch points; the full suite (which exercises
  `UpdateCombatantAsync` with conditions) must stay green after the retype.
- **[Tick mutates many combatants in AdvanceTurn]** → all within the one tracked context +
  `SaveChangesAsync`, so atomic; only combatants with timed conditions actually change.
- **[Expire-at-0 off-by-one]** → define it once: on rollover, `RoundsRemaining -= 1`; remove when it
  reaches 0 or below. A condition set to N is present through N round rollovers. Covered by a test.

## Migration Plan

No schema/data migration. Steps: (1) `ConditionTimer` + backward-compat `CombatantConditions`; (2)
`UpdateCombatantAsync` retype; (3) `AdvanceTurnAsync` tick/expire on rollover; (4) UI per-chip rounds
field; (5) verify. Rollback is a code revert — the JSON column shape is read-compatible in both
directions for the durationless case.

## Verification

Build 0/0; full `dotnet test` green with new real-Postgres tests (timed condition persists; round
rollover decrements + expires at 0; indefinite never ticks; the tick hits all combatants; advancing
*within* a round doesn't tick) + a `CombatantConditions` unit test round-tripping both JSON shapes;
and a Playwright screenshot of a timed condition ("poisoned (2)") counting down across an
advance-to-new-round and expiring.
