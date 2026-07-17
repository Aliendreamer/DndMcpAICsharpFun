# extraction-authority-ladder Specification

## Purpose
TBD - created by archiving change extraction-authority-ladder. Update Purpose after archive.
## Requirements
### Requirement: The 5etools subclass roster SHALL be indexed for name matching

`EntityNameIndex` SHALL load the `subclass[]` array present in every `5etools/class/class-*.json`, indexing each subclass's `name` and `shortName` as `EntityType.Subclass`, in addition to the base-class `class[]` array it already loads. A candidate whose name matches an indexed subclass SHALL resolve to `EntityType.Subclass` (via the existing deterministic force-on-match path) and ground to the matched subclass's canonical name.

#### Scenario: A subclass header matches and is forced to Subclass
- **WHEN** an official book yields a candidate "Path of the Battlerager" (a `subclass[]` entry in `class-barbarian.json`)
- **THEN** it matches the subclass roster and is forced to `EntityType.Subclass` with the canonical name "Path of the Battlerager", not declined

#### Scenario: A short subclass header matches on shortName
- **WHEN** a candidate header is the bare short form (e.g. "Mastermind", a subclass `shortName`)
- **THEN** it matches the indexed subclass `shortName` and resolves to `EntityType.Subclass`

#### Scenario: A base-class name still resolves to Class
- **WHEN** a candidate "Barbarian" matches the base-class `class[]` entry
- **THEN** it resolves to `EntityType.Class` (base classes are indexed before subclasses so the base name wins)

### Requirement: A book-derived IsRealEntity predicate SHALL gate gated-prior no-match candidates in both official and keyless books

The system SHALL provide a deterministic `IsRealEntity` predicate over a candidate, defined as a **structural signature**: a complete stat block, a magic-item signature, or a **subclass-feature-progression** signature (two or more level-gated feature grants). For a gated-prior candidate with no 5etools match, the predicate SHALL govern admission: when `IsRealEntity` is true the candidate is admitted to content-first extraction grounded by its own prose (fields validated by the grounding cascade); when false it is declined as noise. This SHALL apply to keyless books as well as official books — a keyless book's gated-prior candidates that fail the predicate SHALL be declined rather than extracted. The predicate SHALL NOT admit a candidate on prose alone (an entity-like name plus a substantial body); live validation showed a prose-admission branch floods class-feature and lore sub-sections into `Class`/`Race` junk while adding no real entity (genuine prose entities arrive via 5etools matches). Prose homebrew entities that lack a 5etools match are deferred to the Tier-3 web referee, not admitted here.

#### Scenario: A real unindexed structural entity is admitted, not declined
- **WHEN** an official book yields a gated-prior candidate with no 5etools match that satisfies `IsRealEntity` (a stat block, magic item, or subclass-feature progression)
- **THEN** it is admitted to grounded extraction and NOT written to `.declined.json`

#### Scenario: A no-structural-signature heading is declined
- **WHEN** a gated-prior candidate with no 5etools match fails `IsRealEntity` (e.g. "Ability Score Increase", a class-feature sub-section, a race-lore section, a "d6 Resource" table, a "CONTENTS" heading)
- **THEN** it is declined and NOT emitted as an entity

#### Scenario: An empty base-class shell is dropped
- **WHEN** an official book yields a `Class`-prior candidate that is a bare base-class header with no structural signature and no substantial body (e.g. an empty "Barbarian" section header)
- **THEN** it fails `IsRealEntity` and is not emitted as an entity

#### Scenario: Keyless-book noise is now filtered
- **WHEN** a keyless book yields a gated-prior candidate that fails `IsRealEntity` (table/TOC/fragment noise)
- **THEN** it is declined instead of extracted (keyless books previously extracted all such candidates)

#### Scenario: Ungrounded fields on an admitted candidate are rejected
- **WHEN** an admitted no-match candidate is extracted but its emitted fields fail the grounding cascade
- **THEN** the entity is rejected by the cascade (the relaxed gate does not weaken the anti-hallucination guarantee)

### Requirement: A refute-biased web authority referee SHALL label sourceless candidates without dropping them

The system SHALL provide a `IWebAuthorityReferee` over the existing SearXNG client that, for a candidate with no authoritative corroboration (a keyless book, or an official candidate with no 5etools match), determines an authority label. The referee SHALL be refute-biased: it confirms an entity as verified only on a strong authoritative-looking hit, and a miss SHALL downgrade to `homebrew` rather than drop the entity. The referee SHALL be opt-in (toggled off by default) and bounded by a per-call timeout and a by-name cache.

#### Scenario: A keyless entity confirmed by an authoritative hit is verified
- **WHEN** the referee is enabled and a keyless-book entity matches a strong authoritative web result
- **THEN** the entity is labeled `verified-thirdparty` and kept

#### Scenario: A keyless entity with no authoritative hit is kept as homebrew
- **WHEN** the referee is enabled and a keyless-book entity has no authoritative web result
- **THEN** the entity is labeled `homebrew` and kept (never dropped)

#### Scenario: The referee is skipped when disabled
- **WHEN** the referee toggle is off
- **THEN** no web calls are made and sourceless entities retain their non-web label (`canon-unindexed` for official, `homebrew`/unlabeled for keyless per configuration)

### Requirement: Every emitted entity SHALL carry an authority label

Every entity emitted to `dnd_entities` SHALL carry an authority label: `canon` (a 5etools match), `canon-unindexed` (an official book with no 5etools match), `verified-thirdparty` (a keyless entity confirmed by the web referee), or `homebrew` (a keyless entity with no authoritative corroboration). The label SHALL be present in the entity's persisted payload so retrieval can filter or down-weight on it. A match miss SHALL never by itself remove an entity from the canonical `entities`.

#### Scenario: A 5etools-matched entity is labeled canon
- **WHEN** a candidate matches the 5etools index and is extracted
- **THEN** the emitted entity's authority label is `canon`

#### Scenario: An official unindexed entity is labeled canon-unindexed
- **WHEN** an official-book candidate with an entity signature but no 5etools match is extracted and grounded
- **THEN** the emitted entity's authority label is `canon-unindexed`

