## ADDED Requirements

### Requirement: Registry loads books.json at startup
The system SHALL load `5etools/books.json` once at application startup into a singleton `BookSourceRegistry`. Each entry SHALL be indexed by its `id` field (the source key).

#### Scenario: Known source key lookup
- **WHEN** `TryGetBook("XDMG")` is called
- **THEN** returns a `FivetoolsBookInfo` with `SourceKey="XDMG"`, `Name="Dungeon Master's Guide (2024)"`, `Group="core"`, `PublishedYear=2024`, `DisplayAbbr="DMG'24"`

#### Scenario: Unknown source key lookup
- **WHEN** `TryGetBook("HOMEBREW")` is called
- **THEN** returns `null`

#### Scenario: Missing books.json
- **WHEN** `5etools/books.json` does not exist at startup
- **THEN** the application logs a warning and the registry is empty (does not throw)

### Requirement: Registry resolves groups to source key lists
The system SHALL expose `GetByGroup(string group)` returning all source keys whose `group` matches.

#### Scenario: Core group resolution
- **WHEN** `GetByGroup("core")` is called
- **THEN** returns `["PHB","MM","DMG","XPHB","XDMG","XMM"]` (or any set matching group=core in books.json)

#### Scenario: Unknown group
- **WHEN** `GetByGroup("unknown-group")` is called
- **THEN** returns an empty list

### Requirement: Registry resolves MCP intent aliases
The system SHALL expose `ResolveIntent(string intent)` mapping human-readable intents to source key lists. Supported intents: `"core"`, `"core books"`, `"supplement"`, `"supplements"`, `"setting"`, `"2014"`, `"5e"`, `"2024"`, `"5.5e"`, `"srd"`, `"free rules"`.

#### Scenario: Core books intent
- **WHEN** `ResolveIntent("core books")` is called
- **THEN** returns the same list as `GetByGroup("core")`

#### Scenario: Year-based intent 2024
- **WHEN** `ResolveIntent("2024")` is called
- **THEN** returns all source keys where `PublishedYear >= 2024`

#### Scenario: Year-based intent 2014
- **WHEN** `ResolveIntent("2014")` or `ResolveIntent("5e")` is called
- **THEN** returns all source keys where `PublishedYear < 2020`

#### Scenario: SRD intent
- **WHEN** `ResolveIntent("srd")` or `ResolveIntent("free rules")` is called
- **THEN** returns the string sentinel `"srd52"` (signals a payload flag filter, not a source key list)

#### Scenario: Unrecognised intent
- **WHEN** `ResolveIntent("gibberish")` is called
- **THEN** returns an empty list

### Requirement: Display abbreviation derivation
The system SHALL compute `DisplayAbbr` as: strip a leading `X` from the source key (if present), then append `'` and the last two digits of `PublishedYear`.

#### Scenario: 2024 edition key
- **WHEN** source key is `XDMG`, published year is 2024
- **THEN** `DisplayAbbr` is `"DMG'24"`

#### Scenario: 2014 edition key without X prefix
- **WHEN** source key is `PHB`, published year is 2014
- **THEN** `DisplayAbbr` is `"PHB'14"`

#### Scenario: Supplement without X prefix
- **WHEN** source key is `TCE`, published year is 2020
- **THEN** `DisplayAbbr` is `"TCE'20"`
