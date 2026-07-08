## ADDED Requirements

### Requirement: Entity dedup key

The system SHALL compute a dedup key for an entity as the tuple
`(EntityNameIndex.Normalize(name), Type, Edition)`. Two entities SHALL be considered
duplicates of each other if and only if their dedup keys are equal but their canonical `Id`s
differ. Entities whose editions differ SHALL NOT be duplicates even when name and type match.
The key SHALL be derived from the entity's own fields and MUST NOT require a lookup in the
5etools index, so that homebrew entities absent from that index dedup correctly.

#### Scenario: Same name, type and edition, different id are duplicates

- **WHEN** two entities have equal normalized name, equal `Type`, equal `Edition`, and
  different canonical `Id`
- **THEN** they SHALL share a dedup key and be treated as duplicates

#### Scenario: Different editions are not duplicates

- **WHEN** two entities have equal normalized name and `Type` but `Edition` `Edition2014` vs
  `Edition2024`
- **THEN** their dedup keys SHALL differ and they SHALL NOT be treated as duplicates

#### Scenario: Homebrew entity not in 5etools still keys

- **WHEN** an entity's name is absent from the 5etools `EntityNameIndex`
- **THEN** its dedup key SHALL still be computed from its own normalized name, type, and edition

### Requirement: Duplicate winner selection

The system SHALL provide a pure `DuplicateResolver` that, given a group of entities sharing a
dedup key, returns exactly one winner using authority-first precedence, evaluated in order
until a difference decides: (1) `BookType` authority `Core > Supplement > Adventure > Setting
> Unknown`; (2) `DataSource` authority, where authoritative sources (`5etools-backfill`,
hand-authored) outrank raw LLM-parsed; (3) not-`NeedsReview` outranks `NeedsReview`; (4) longer
`CanonicalText`; (5) lexicographically smallest `Id`. The resolver MUST be deterministic —
the same group SHALL yield the same winner regardless of input order.

#### Scenario: Core book beats supplement

- **WHEN** a group contains a `Core`-book entity and a `Supplement`-book entity
- **THEN** the resolver SHALL select the `Core`-book entity

#### Scenario: Authority outranks the review flag

- **WHEN** a group contains a `Core`-book entity flagged `NeedsReview` and a `Supplement`-book
  entity not flagged
- **THEN** the resolver SHALL select the `Core`-book entity despite its review flag

#### Scenario: Deterministic tiebreak on id

- **WHEN** two entities in a group are equal on all higher-precedence signals
- **THEN** the resolver SHALL select the one with the lexicographically smallest `Id`,
  regardless of the order the group was supplied in

### Requirement: Duplicate report endpoint

The system SHALL expose `GET /admin/retrieval/entities/duplicates`, guarded by the admin API
key, that scans the entire `dnd_entities` corpus, groups points by dedup key, and returns every
group containing more than one member as `{ key, winnerId, loserIds }` where the winner is
chosen by `DuplicateResolver`. The endpoint SHALL NOT modify any data.

#### Scenario: Reports duplicate groups without mutation

- **WHEN** the corpus contains two Fireball entities sharing a dedup key
- **THEN** the response SHALL include a group listing the resolver winner as `winnerId` and the
  other as a `loserId`, and no points SHALL be deleted

#### Scenario: Unique entities are not reported

- **WHEN** an entity has no other entity sharing its dedup key
- **THEN** it SHALL NOT appear in the duplicates report

### Requirement: Destructive compact endpoint

The system SHALL expose `POST /admin/retrieval/entities/compact`, guarded by the admin API key,
that groups the entire `dnd_entities` corpus by dedup key. By default (dry-run) it SHALL return
the same report shape as the duplicates endpoint without deleting anything. When invoked with
`?apply=true` it SHALL delete only the loser points from `dnd_entities` and return the groups it
acted on. The endpoint MUST NOT rewrite or delete any canonical JSON file.

#### Scenario: Dry-run reports without deleting

- **WHEN** the compact endpoint is called without `apply=true`
- **THEN** it SHALL return the duplicate groups and delete no points

#### Scenario: Apply deletes only losers

- **WHEN** the compact endpoint is called with `apply=true` for a group of two duplicates
- **THEN** the resolver winner SHALL remain in `dnd_entities` and only the loser point SHALL be
  deleted

#### Scenario: Canonical files are never modified

- **WHEN** the compact endpoint is called with `apply=true`
- **THEN** no file under `books/canonical/` SHALL be created, modified, or deleted
