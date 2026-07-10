## ADDED Requirements

### Requirement: Persisted combat and combatant model

The system SHALL persist combats in two relational tables. A `Combat` SHALL carry a `CampaignId`, a
`UserId`, a `Name`, an `Edition` (`Y2014` or `Y2024`), a `Status` (`Active` or `Ended`), a `Round`
(starting at 1), a `CurrentTurnIndex`, a `CreatedAt`, and a nullable `EndedAt`. A `Combatant` SHALL
carry a foreign key to its `Combat`, a nullable `HeroId` (set when drafted from a party hero), a
`Name`, an `IsPlayer` flag, a nullable `InitiativeRoll`, an `InitiativeModifier`, a `MaxHp`, a
`CurrentHp`, a nullable `Ac`, a set of conditions serialized as JSON, and an `AddedOrder` insertion
sequence. Deleting a `Combat` SHALL cascade-delete its combatants; deleting a `Campaign` SHALL
cascade-delete its combats. The schema SHALL be created by an additive EF migration applied at
startup.

#### Scenario: A combat with combatants round-trips through storage

- **WHEN** a combat is started and combatants are added, then the combat is read back
- **THEN** the combat and all its combatants SHALL be returned with their persisted fields intact

#### Scenario: Deleting a combat removes its combatants

- **WHEN** a combat is deleted
- **THEN** all of its combatants SHALL be removed and none SHALL remain orphaned

#### Scenario: Deleting a campaign removes its combats

- **WHEN** a campaign with combats is deleted
- **THEN** all of that campaign's combats and their combatants SHALL be removed

### Requirement: Combat persistence is ownership-scoped

The `CombatRepository` SHALL scope every read and command by `CampaignId` and the calling `UserId`,
and combat-targeted commands additionally by the combat `Id`. Reads SHALL return only the caller's
combats for that campaign. `AddCombatantAsync`, `UpdateCombatantAsync`, `RemoveCombatantAsync`,
`AdvanceTurnAsync`, `EndAsync`, and `DeleteAsync` SHALL affect a combat only when it belongs to the
caller and the given campaign; a combat not owned by the caller SHALL be unaffected (no rows
changed) and its contents SHALL NOT be returned or modified.

#### Scenario: Only the caller's combats are returned

- **WHEN** `GetActiveAsync(campaignId, userId)` or `GetHistoryAsync(campaignId, userId)` is called
- **THEN** it SHALL return only combats with that `CampaignId` and `UserId`

#### Scenario: Another user's combat cannot be mutated

- **WHEN** any command is called against a combat that belongs to a different user or campaign
- **THEN** no row SHALL be changed and the other user's combat SHALL remain unchanged

### Requirement: At most one active combat per campaign

The system SHALL allow at most one combat with `Status` `Active` per campaign at a time.
`StartAsync` SHALL create a new active combat only when the campaign has no active combat; otherwise
it SHALL reject the request and SHALL NOT create a second active combat.

#### Scenario: Starting a combat when none is active

- **WHEN** `StartAsync` is called for a campaign with no active combat
- **THEN** a new `Active` combat SHALL be created with `Round` 1

#### Scenario: Starting a second combat is rejected

- **WHEN** `StartAsync` is called for a campaign that already has an active combat
- **THEN** the request SHALL be rejected and no second active combat SHALL be created

### Requirement: Ended combats are retained as history

When a combat ends, its record and combatants SHALL be retained. `GetHistoryAsync` SHALL return the
campaign's ended combats newest-first. The active combat (if any) SHALL be excluded from the history
listing.

#### Scenario: Ended combats are listed newest-first

- **WHEN** two combats have been ended and `GetHistoryAsync(campaignId, userId)` is called
- **THEN** it SHALL return both ended combats ordered by end time, newest first

### Requirement: Combatant drafting from party, encounter, and manual sources

`CombatService` SHALL draft combatants into a combat from three sources. From the campaign's party
heroes it SHALL create player combatants with `IsPlayer` true, `HeroId` set, and `Name`, `MaxHp`,
`CurrentHp`, and `Ac` taken from each hero's latest snapshot sheet, leaving `InitiativeRoll` unset.
From a built encounter's monsters it SHALL create monster combatants with `IsPlayer` false and
`InitiativeRoll` auto-rolled. It SHALL also support manual add of a single combatant that is either
player-style (initiative entered manually) or monster-style (initiative auto-rolled).

#### Scenario: Drafting the party links heroes and leaves initiative unset

- **WHEN** the party is drafted into a combat
- **THEN** each party hero SHALL become a player combatant with its `HeroId`, HP, and AC from the latest sheet, and its `InitiativeRoll` SHALL be unset

#### Scenario: Drafting encounter monsters auto-rolls initiative

- **WHEN** monsters from a built encounter are drafted into a combat
- **THEN** each monster SHALL become a combatant with `IsPlayer` false and an `InitiativeRoll` produced by the dice roller

### Requirement: Initiative ordering and turn advancement

The combat SHALL order combatants by `InitiativeRoll` descending, then `InitiativeModifier`
descending, then player-before-monster, then `AddedOrder` ascending; combatants without an
`InitiativeRoll` SHALL sort last. Monster initiative SHALL be auto-rolled as `d20 + InitiativeModifier`
using the injected `IRandomSource`, with `InitiativeModifier` defaulting to 0 and remaining
DM-editable. Advancing the turn SHALL move `CurrentTurnIndex` to the next combatant in order,
wrapping to the first and incrementing `Round` when it passes the end.

#### Scenario: Advancing past the last combatant wraps and bumps the round

- **WHEN** the turn is advanced while the current turn is on the last combatant in order
- **THEN** `CurrentTurnIndex` SHALL wrap to the first combatant and `Round` SHALL increment by 1

#### Scenario: Monster initiative is auto-rolled over the injected RNG

- **WHEN** a monster combatant is drafted with a seeded `IRandomSource`
- **THEN** its `InitiativeRoll` SHALL equal the seeded `d20 + InitiativeModifier` result deterministically

### Requirement: Combatants carry a multi-select set of D&D conditions

A combatant SHALL be able to hold zero or more conditions drawn from a fixed enum of the 15 standard
D&D conditions (Blinded, Charmed, Deafened, Frightened, Grappled, Incapacitated, Invisible,
Paralyzed, Petrified, Poisoned, Prone, Restrained, Stunned, Unconscious, Exhaustion). The condition
set SHALL be edition-independent and SHALL persist as part of the combatant record.

#### Scenario: A combatant's conditions round-trip through storage

- **WHEN** a combatant is saved with two conditions set and then read back
- **THEN** both conditions SHALL be present and no others

### Requirement: DM-approval-gated HP write-back on combat end

Ending a combat SHALL open a review of each player combatant's proposed post-fight HP, editable by
the DM, and SHALL NOT write to any hero sheet until the DM approves. On approval, for each player
combatant that has a `HeroId`, the system SHALL append a new `HeroSnapshot` that clones the hero's
latest sheet with `CurrentHitPoints` set to the approved value; prior snapshots SHALL be preserved.
The combat SHALL then be marked `Ended`. Cancelling the review SHALL leave the combat active and
write nothing.

#### Scenario: Approving write-back appends a new snapshot per linked hero

- **WHEN** the DM approves the end-combat review
- **THEN** each player combatant with a `HeroId` SHALL get a new `HeroSnapshot` with the approved `CurrentHitPoints`, prior snapshots SHALL remain, and the combat SHALL be `Ended`

#### Scenario: Cancelling writes nothing and keeps the combat active

- **WHEN** the DM cancels the end-combat review
- **THEN** no hero snapshot SHALL be written and the combat SHALL remain `Active`

#### Scenario: A player combatant without a linked hero does not write back

- **WHEN** the end-combat review is approved and a player combatant has no `HeroId`
- **THEN** no snapshot SHALL be written for that combatant

### Requirement: Per-campaign play page hosts the table-play tools

The system SHALL provide an authorized play page at `/campaigns/{id}/table` that resolves the
signed-in user from the `NameIdentifier` claim, gates access by campaign ownership (redirecting when
the campaign is not the caller's), and hosts the dice roller, encounter panel, campaign log, and the
initiative tracker together. The campaign detail page SHALL link to it and SHALL NOT embed those
table-play tools itself.

#### Scenario: The owner reaches the play page

- **WHEN** the campaign owner navigates to `/campaigns/{id}/table`
- **THEN** the page SHALL render the dice roller, encounter panel, campaign log, and initiative tracker

#### Scenario: A non-owner is redirected

- **WHEN** a user who does not own the campaign navigates to its play page
- **THEN** access SHALL be denied by redirect and no combat data SHALL be shown

### Requirement: Active combat rehydrates after reload

Because combat state is persisted, the initiative tracker SHALL rehydrate the campaign's active
combat from storage on load, so a page reload or a dropped Blazor circuit SHALL NOT lose an
in-progress fight. Rendering of combatant data SHALL be null-safe and SHALL NOT throw out of the
Blazor render loop on partial or empty data.

#### Scenario: Reloading restores the in-progress combat

- **WHEN** the play page is reloaded while a combat is active
- **THEN** the initiative tracker SHALL show the same combatants, round, and current turn from storage
