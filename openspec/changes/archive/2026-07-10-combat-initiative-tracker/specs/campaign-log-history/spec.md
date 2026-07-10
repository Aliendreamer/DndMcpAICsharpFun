## MODIFIED Requirements

### Requirement: Campaign log entry persisted as a unified timeline row

The system SHALL persist campaign log entries in a single table as `CampaignLogEntry` with a
`CampaignId`, a `UserId`, a `Kind` of `Roll`, `Encounter`, or `Combat`, an optional `Label`, a
`Hidden` flag, a `CreatedAt` timestamp, and a JSON payload. The roll payload SHALL capture the
expression, breakdown, total, dice, kept dice, and mode; the encounter payload SHALL capture the
difficulty, XP, party levels, monsters, and note; the combat payload SHALL capture the combat name,
edition, round count, and combatant summary. The schema SHALL be created by an EF migration applied
at startup.

#### Scenario: A roll entry round-trips through storage

- **WHEN** a roll is saved and then read back
- **THEN** its payload SHALL deserialize to the same expression, breakdown, total, and dice

#### Scenario: An encounter entry round-trips through storage

- **WHEN** an encounter is saved and then read back
- **THEN** its payload SHALL deserialize to the same difficulty, monsters, and XP

#### Scenario: A combat entry round-trips through storage

- **WHEN** a combat breadcrumb is saved and then read back
- **THEN** its payload SHALL deserialize to the same combat name, round count, and combatant summary

### Requirement: Encounters are saved to the log explicitly, optionally hidden

The system SHALL provide a **play (table) page** panel to build an encounter (difficulty, edition,
optional theme; party from the campaign's heroes) and SHALL persist an `Encounter` log entry only on
an explicit save action. The save SHALL allow marking the entry hidden and adding a label. A built
encounter SHALL NOT be persisted until the user saves it.

#### Scenario: Building does not persist; saving does

- **WHEN** the user builds an encounter but does not click save
- **THEN** no `Encounter` entry SHALL be persisted

#### Scenario: Saving a hidden encounter

- **WHEN** the user builds an encounter and saves it with the hidden option set
- **THEN** an `Encounter` entry SHALL be persisted with `Hidden` true and its assessed difficulty + monsters in the payload

### Requirement: Campaign timeline shows entries with hidden/reveal

The campaign **play (table) page** SHALL show the campaign log newest-first, rendering roll entries
(label, breakdown, total), encounter entries (label, difficulty, monsters), and combat entries
(label, combat name, round count). Hidden entries SHALL be shown to the owner with a hidden indicator
and a reveal action that clears the hidden flag. The user SHALL be able to delete an entry.

#### Scenario: Revealing a hidden entry clears its hidden flag

- **WHEN** the user clicks reveal on a hidden entry
- **THEN** the entry's `Hidden` flag SHALL become false and it SHALL render as a normal (non-hidden) entry

#### Scenario: Deleting an entry removes it from the log

- **WHEN** the user deletes an entry
- **THEN** it SHALL no longer appear in the campaign log

## ADDED Requirements

### Requirement: Ending a combat drops a combat breadcrumb entry

When a combat is ended, the system SHALL persist a `Combat`-kind `CampaignLogEntry` for that campaign
summarizing the fight (combat name, round count, combatant summary), so the timeline records that the
fight happened. The breadcrumb SHALL be saved revealed (not hidden) and SHALL be ownership-scoped like
every other log entry.

#### Scenario: Ending a combat records a breadcrumb

- **WHEN** a combat is ended for a campaign
- **THEN** a `Combat` log entry SHALL be persisted for that campaign and SHALL appear in the campaign log
