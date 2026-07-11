# scratch-surface Specification

## Purpose
TBD - created by archiving change scratch-surface. Update Purpose after archive.
## Requirements
### Requirement: Global scratch page for off-campaign dice and encounters

The system SHALL provide an authorized page at `/scratch`, reached from the sidebar, that lets the
signed-in user roll dice and build encounters without opening a campaign. The page SHALL host the
dice roller and an encounter builder only — it SHALL NOT host the initiative tracker or any
combat/campaign-log surface.

#### Scenario: The scratch page is reachable and campaign-free

- **WHEN** a signed-in user navigates to `/scratch`
- **THEN** the page SHALL render the dice roller and the encounter builder, and SHALL NOT require or reference any campaign

### Requirement: Scratch dice rolls are ephemeral

Dice rolled on the scratch page SHALL behave exactly like the campaign dice roller for computing and
displaying results, but SHALL NOT be persisted anywhere (no campaign-log entry) — they appear only in
the session's recent-rolls list.

#### Scenario: A scratch roll is not persisted

- **WHEN** the user rolls dice on the scratch page
- **THEN** the result and breakdown SHALL be shown, and no log entry SHALL be written to any store

### Requirement: Scratch encounters build against a typed party

Because there is no campaign, the scratch encounter builder SHALL take the party as a size and a
level (N characters at level L), build the party levels as `[L]` repeated `N` times, and assess the
encounter against that explicit party using the shared encounter service. It SHALL show the built
encounter's difficulty and monsters, and SHALL NOT offer to save the result (there is no campaign
log).

#### Scenario: Building from size and level

- **WHEN** the user enters a party size and level and builds an encounter
- **THEN** the encounter SHALL be assessed against a party of that size all at that level, and its difficulty and monsters SHALL be shown

#### Scenario: No save affordance off-campaign

- **WHEN** an encounter is built on the scratch page
- **THEN** there SHALL be no "save to log" action (nothing is persisted)

