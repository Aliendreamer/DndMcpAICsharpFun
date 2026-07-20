## ADDED Requirements

### Requirement: Resolve class features by level

The system SHALL resolve the `"class features"` feature by reading, for each of the character's classes, the projected `<slug>.table.<class-slug>` class-progression table and returning the cumulative base-class features gained at levels 1 through that class's current level plus the current proficiency bonus, cited to the table. Multiclass characters SHALL produce one component per class. A class whose projected table is absent SHALL resolve `needsReview`, never a fabricated value.

#### Scenario: Single-class features resolved cumulatively

- **WHEN** `resolve_character_feature` is called with `"class features"` for a level-6 single-class Fighter
- **THEN** the resolved value lists the Fighter's base features gained through level 6 (including Extra Attack at 5 and the Ability Score Improvement at 6) with the current proficiency bonus `+3`, and each cited to `phb14.table.fighter`

#### Scenario: Unprojected class marked needsReview, not fabricated

- **WHEN** a class has no projected `<slug>.table.<class-slug>` table (e.g. a non-projected book)
- **THEN** that class's result is `needsReview` and no feature list is invented for it

### Requirement: Resolve subclass granted spells

The system SHALL resolve the `"subclass spells"` feature by reading the projected `<slug>.table.<subclass-slug>-spells` table for the character's `Subclass` and returning the spells granted at grant-levels less than or equal to the character's level, cited to the table. A subclass whose table is absent SHALL resolve `needsReview`; a subclass whose table exists but grants no spells at or below the level SHALL resolve an honest "none" with confidence `ok`.

#### Scenario: Domain spells resolved up to level

- **WHEN** `resolve_character_feature` is called with `"subclass spells"` for a level-5 Life Domain Cleric
- **THEN** the resolved value contains the always-prepared domain spells granted through level 5 (bless, cure wounds, lesser restoration, spiritual weapon, beacon of hope, revivify), cited to the subclass-spells table

#### Scenario: Subclass with no granted spells resolves an honest none

- **WHEN** the character's subclass grants no additional spells (e.g. a Champion Fighter) and its spells table is absent or empty
- **THEN** the result is `needsReview` when no table exists, or value "none" with confidence `ok` when the table exists but is empty — never a fabricated spell list

### Requirement: Resolution tool advertises the new features

The `resolve_character_feature` MCP tool description SHALL list `"class features"` and `"subclass spells"` among its supported features so the chat model routes those questions to the deterministic resolver.

#### Scenario: Tool description includes the new features

- **WHEN** the `resolve_character_feature` tool schema is built
- **THEN** its description names `"class features"` and `"subclass spells"` alongside the existing supported features
