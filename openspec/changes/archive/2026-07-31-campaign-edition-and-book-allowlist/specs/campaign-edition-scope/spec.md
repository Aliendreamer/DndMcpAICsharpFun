## ADDED Requirements

### Requirement: A campaign SHALL declare its rules canon

`Campaign` SHALL carry a non-null `Edition` (`DndVersion`). It is the campaign's **rules canon** and SHALL be the authority that resolves a mechanics conflict when books from both canons are in scope — for example a grappling question when both `PHB` and `XPHB` are allowed.

The edition SHALL come from campaign state and SHALL NOT be supplied by the language model as a tool argument. A question the campaign already answers must not be delegated to the model.

#### Scenario: A campaign has an edition

- **WHEN** a campaign is created without specifying one
- **THEN** its `Edition` is `Edition2014`

#### Scenario: A 2024 campaign records its canon

- **WHEN** a campaign is created with `Edition2024`
- **THEN** that value round-trips through persistence unchanged

### Requirement: A campaign SHALL declare its in-play source books

`Campaign` SHALL carry a set of source-book keys — its **allowlist** — controlling which books are available to it at all. Entries SHALL be `BookCatalog` keys, never free-text display names, so the set inherits the catalog-drift guard and an unknown key is detectable rather than silently matching nothing.

Availability (the allowlist) and rules canon (the edition) are SEPARATE: a campaign MAY allow books from both canons, which is the normal case for a 2024 table playing in a 2014-era setting.

#### Scenario: The allowlist holds catalog keys

- **WHEN** a campaign allows Eberron
- **THEN** its allowlist contains the key `ERLW`, not the display name `Eberron: Rising from the Last War`

#### Scenario: A campaign may mix canons

- **WHEN** a campaign has `Edition2024` and an allowlist of `XPHB`, `XDMG`, `ERLW`, `XGE`
- **THEN** this is valid and is persisted as given — a 2024 table playing Eberron with Xanathar's

#### Scenario: An unknown key is rejected

- **WHEN** a campaign is saved with an allowlist entry that is not a `BookCatalog` key
- **THEN** the write is rejected with a clear error rather than stored

### Requirement: New campaigns SHALL default to core-only for their edition

A campaign created without an explicit allowlist SHALL receive the core trio for its edition: `PHB`, `DMG`, `MM` for `Edition2014`; `XPHB`, `XDMG`, `XMM` for `Edition2024`. Supplements and settings are opt-in.

#### Scenario: A new 2014 campaign gets the 2014 core

- **WHEN** a campaign is created with `Edition2014` and no allowlist
- **THEN** its allowlist is exactly `PHB`, `DMG`, `MM`

#### Scenario: A new 2024 campaign gets the revised core

- **WHEN** a campaign is created with `Edition2024` and no allowlist
- **THEN** its allowlist is exactly `XPHB`, `XDMG`, `XMM`

### Requirement: Existing campaigns SHALL retain their current access

The migration SHALL seed every pre-existing campaign with `Edition2014` and an allowlist containing every source-book key registered at migration time, so existing campaigns behave exactly as they do today.

Existing campaigns SHALL NOT receive the core-only default. Applying it retroactively would silently remove thirteen books — including all five setting books — from live campaigns.

#### Scenario: An existing campaign keeps every book

- **WHEN** the migration runs against a campaign created before this change
- **THEN** that campaign's allowlist contains all source-book keys registered at migration time, and its edition is `Edition2014`

#### Scenario: Behaviour is unchanged for existing campaigns

- **WHEN** an existing campaign asks a question after the migration
- **THEN** the set of books available to it is the same as before the migration

### Requirement: A single resolver SHALL expose the effective scope

The system SHALL provide one resolver that maps a campaign to its effective retrieval scope, so no consumer re-derives the rule. It SHALL expose the campaign's edition and its allowed book keys, and SHALL distinguish:

- **mechanics scope** — the allowlist restricted to the campaign's edition
- **lore scope** — the allowlist irrespective of edition

Lore is edition-portable and mechanics are not: a setting's history is equally true under either canon, whereas a subclass is not. Consumers in later slices intersect their own source lists with these.

#### Scenario: Mechanics scope is edition-restricted

- **WHEN** a campaign is `Edition2024` and allows `XPHB`, `XDMG`, `ERLW`
- **THEN** the mechanics scope contains `XPHB` and `XDMG` but not the 2014-canon `ERLW`

#### Scenario: Lore scope ignores edition

- **WHEN** the same campaign's lore scope is requested
- **THEN** it contains `ERLW`, so a 2024 Eberron table keeps its setting

#### Scenario: Resolution is deterministic and needs no model call

- **WHEN** the scope for a campaign is resolved
- **THEN** it is computed from stored campaign state alone, with no language-model involvement
