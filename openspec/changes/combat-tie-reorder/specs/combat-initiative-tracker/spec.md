## MODIFIED Requirements

### Requirement: Initiative ordering and turn advancement

The combat SHALL order combatants by `InitiativeRoll` descending, then `InitiativeModifier`
descending, then player-before-monster, then `AddedOrder` ascending; combatants without an
`InitiativeRoll` SHALL sort last. Monster initiative SHALL be auto-rolled as `d20 + InitiativeModifier`
using the injected `IRandomSource`, with `InitiativeModifier` defaulting to 0 and remaining
DM-editable. The current turn SHALL be tracked by combatant identity (`CurrentTurnCombatantId`, null
before the first advance meaning the top of the order is current). Advancing the turn SHALL move the
current turn to the next combatant in order, wrapping to the first and incrementing `Round` when it
passes the end. When advancing rolls the round over (increments `Round`), every combatant's timed
conditions SHALL decrement by 1 and any that reach 0 SHALL be removed; indefinite conditions SHALL NOT
change. Removing the combatant whose turn it currently is SHALL re-anchor the current turn to the next
combatant in order (wrapping to the first if it was last, and clearing the current turn when it was
the last remaining combatant), so the turn marker never points at a removed combatant. The DM SHALL be
able to manually reorder combatants the sort treats as tied — those equal on `InitiativeRoll`,
`InitiativeModifier`, and side (player-before-monster) — by swapping their `AddedOrder`; such a reorder
SHALL NOT change the relative order of combatants the sort can already distinguish, SHALL be a no-op
when the combatant has no tied neighbor in the requested direction, and SHALL NOT change the current
turn.

#### Scenario: Advancing past the last combatant wraps and bumps the round

- **WHEN** the turn is advanced while the current turn is on the last combatant in order
- **THEN** the current turn SHALL wrap to the first combatant and `Round` SHALL increment by 1

#### Scenario: Monster initiative is auto-rolled over the injected RNG

- **WHEN** a monster combatant is drafted with a seeded `IRandomSource`
- **THEN** its `InitiativeRoll` SHALL equal the seeded `d20 + InitiativeModifier` result deterministically

#### Scenario: Removing the current combatant moves the turn to the next in order

- **WHEN** the combatant whose turn it currently is is removed while others remain
- **THEN** the current turn SHALL become the next combatant in order (not the top), and `Round` SHALL NOT change

#### Scenario: Removing the last remaining combatant clears the current turn

- **WHEN** the only remaining combatant (the current one) is removed
- **THEN** the current turn SHALL be cleared (no combatant is current)

#### Scenario: A round rollover ticks and expires timed conditions

- **WHEN** advancing rolls the round over and combatants hold timed conditions
- **THEN** each timed condition's rounds-remaining SHALL decrement by 1, any reaching 0 SHALL be removed, and indefinite conditions SHALL be unchanged

#### Scenario: Advancing within a round does not tick conditions

- **WHEN** the turn advances without rolling the round over
- **THEN** no condition's rounds-remaining SHALL change

#### Scenario: Reordering swaps two tied combatants

- **WHEN** the DM moves one of two combatants that are tied (equal initiative roll, modifier, and side) toward the other
- **THEN** the two SHALL swap places in the order, and the current turn SHALL be unchanged

#### Scenario: Reordering against a non-tied neighbor is a no-op

- **WHEN** the DM moves a combatant toward a neighbor that the sort can already distinguish (e.g. a different initiative roll)
- **THEN** the order SHALL be unchanged
