## MODIFIED Requirements

### Requirement: Combatant drafting from party, encounter, and manual sources

`CombatService` SHALL draft combatants into a combat from three sources. From the campaign's party
heroes it SHALL create player combatants with `IsPlayer` true, `HeroId` set, and `Name`, `MaxHp`,
`CurrentHp`, and `Ac` taken from each hero's latest snapshot sheet, leaving `InitiativeRoll` unset.
From a built encounter's monsters it SHALL create monster combatants with `IsPlayer` false,
`InitiativeRoll` auto-rolled, and `MaxHp`/`CurrentHp` set from the monster's stat block — the book
**average** by default, or, when the caller requests rolled HP, **app-rolled** from the monster's HP
formula (parsed and rolled through the dice roller), falling back to the average when no usable
formula is present and to 0 (DM-editable) when the stat block has neither. It SHALL also support
manual add of a single combatant that is either player-style (initiative entered manually) or
monster-style (initiative auto-rolled).

#### Scenario: Drafting the party links heroes and leaves initiative unset

- **WHEN** the party is drafted into a combat
- **THEN** each party hero SHALL become a player combatant with its `HeroId`, HP, and AC from the latest sheet, and its `InitiativeRoll` SHALL be unset

#### Scenario: Drafting encounter monsters auto-rolls initiative

- **WHEN** monsters from a built encounter are drafted into a combat
- **THEN** each monster SHALL become a combatant with `IsPlayer` false and an `InitiativeRoll` produced by the dice roller

#### Scenario: Drafting a monster uses the book-average HP by default

- **WHEN** a monster with an average HP in its stat block is drafted with rolled-HP off
- **THEN** its `MaxHp` and `CurrentHp` SHALL be set to that average

#### Scenario: Drafting a monster with rolled HP rolls the formula

- **WHEN** a monster with a valid HP formula is drafted with rolled-HP on, over a seeded RNG
- **THEN** its `MaxHp` SHALL equal the deterministically-rolled formula result and `CurrentHp` SHALL equal `MaxHp`

#### Scenario: Rolled HP falls back to average when the formula is unusable

- **WHEN** a monster with an average but no usable HP formula is drafted with rolled-HP on
- **THEN** its `MaxHp` SHALL be set to the average

### Requirement: Initiative ordering and turn advancement

The combat SHALL order combatants by `InitiativeRoll` descending, then `InitiativeModifier`
descending, then player-before-monster, then `AddedOrder` ascending; combatants without an
`InitiativeRoll` SHALL sort last. Monster initiative SHALL be auto-rolled as `d20 + InitiativeModifier`
using the injected `IRandomSource`, with `InitiativeModifier` defaulting to 0 and remaining
DM-editable. The current turn SHALL be tracked by combatant identity (`CurrentTurnCombatantId`, null
before the first advance meaning the top of the order is current). Advancing the turn SHALL move the
current turn to the next combatant in order, wrapping to the first and incrementing `Round` when it
passes the end. Removing the combatant whose turn it currently is SHALL re-anchor the current turn to
the next combatant in order (wrapping to the first if it was last, and clearing the current turn when
it was the last remaining combatant), so the turn marker never points at a removed combatant.

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
