# campaign-log-history Specification

## Purpose
TBD - created by archiving change campaign-log-history. Update Purpose after archive.
## Requirements
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

### Requirement: Campaign log is ownership-scoped

The `CampaignLogRepository` SHALL scope every read and command by both `CampaignId` and the calling
`UserId`. `GetByCampaignAsync` SHALL return only the caller's entries for that campaign, most recent
first. `RevealAsync` and `DeleteAsync` SHALL affect an entry only when it belongs to the caller and
the given campaign; an entry not owned by the caller SHALL be unaffected (no rows changed) and its
contents SHALL NOT be returned or modified.

#### Scenario: Only the caller's entries are returned

- **WHEN** `GetByCampaignAsync(campaignId, userId)` is called
- **THEN** it SHALL return only entries with that `CampaignId` and `UserId`, ordered newest-first

#### Scenario: Another user's entry cannot be revealed or deleted

- **WHEN** `RevealAsync` or `DeleteAsync` is called with an entry id that belongs to a different user
- **THEN** no row SHALL be changed and the other user's entry SHALL remain unchanged

### Requirement: Every dice roll is auto-logged with its label

When the dice roller is used in a campaign context, the system SHALL persist a `Roll` log entry for
every successful roll — recording the roll result and the optional label (the roll's reason/skill,
e.g. a skill check or damage) — in addition to any in-session display. Auto-logged rolls SHALL be
saved revealed (not hidden). No roll SHALL be silently dropped from the log.

#### Scenario: Rolling persists a labelled roll entry

- **WHEN** the user rolls with a label of "Deception"
- **THEN** a `Roll` entry SHALL be persisted for that campaign with the label "Deception" and the roll's result, and SHALL be visible in the campaign log

#### Scenario: Rolls are logged even without a label

- **WHEN** the user rolls with no label
- **THEN** a `Roll` entry SHALL still be persisted (label empty)

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

### Requirement: Ending a combat drops a combat breadcrumb entry

When a combat is ended, the system SHALL persist a `Combat`-kind `CampaignLogEntry` for that campaign
summarizing the fight (combat name, round count, combatant summary), so the timeline records that the
fight happened. The breadcrumb SHALL be saved revealed (not hidden) and SHALL be ownership-scoped like
every other log entry.

#### Scenario: Ending a combat records a breadcrumb

- **WHEN** a combat is ended for a campaign
- **THEN** a `Combat` log entry SHALL be persisted for that campaign and SHALL appear in the campaign log

