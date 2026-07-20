# character-fact-resolution Specification

## Purpose
TBD - created by archiving change character-fact-resolution. Update Purpose after archive.
## Requirements
### Requirement: Resolved-choices slot on the character sheet

`CharacterSheet` SHALL store resolved player choices as structured references in a `ResolvedChoices`
slot (e.g. `ancestry → "phb14.choiceset.draconic-ancestry:Red"`), separate from the flat fields and
the freeform `Features` list. Existing `HeroSnapshot` JSON SHALL remain readable (the field is
optional/empty when absent). `Level` remains an `int` in this slice.

#### Scenario: A character records its ancestry choice
- **WHEN** a Dragonborn hero selects the Red ancestry
- **THEN** `CharacterSheet.ResolvedChoices["ancestry"]` is `"phb14.choiceset.draconic-ancestry:Red"`

#### Scenario: Old snapshots still load
- **WHEN** a `HeroSnapshot` saved before this change is loaded
- **THEN** it deserializes successfully with `ResolvedChoices` empty (no migration error)

### Requirement: Deterministic character-fact resolution

A `CharacterResolutionService.resolve(heroId, feature)` SHALL compute a character-specific fact
deterministically from structured facts + character state, returning `{ value, components[],
provenance[], confidence }`. For `feature = "breath weapon"` it SHALL compose: the resolved
ancestry's table row (damage type, breath area, save ability) + the character's level → breath
damage dice (tier map: L1–5 = 1d10, L6–10 = 2d6, L11–15 = 3d6, L16–20 = 4d6) + the save DC
(`8 + proficiency bonus + Constitution modifier`). Each component SHALL carry its own provenance.
The chat LLM SHALL NOT compute these rule values; it only orchestrates the call and renders the
result.

#### Scenario: Red Dragonborn breath weapon at level 11
- **WHEN** `resolve(hero, "breath weapon")` is called for a Red Dragonborn, level 11, Con modifier +3
- **THEN** the result is fire damage, 15-ft cone, Dex save, DC 15 (8 + 4 + 3), 3d6, with provenance for each component

#### Scenario: Damage scales by tier
- **WHEN** the same character is resolved at level 3 vs level 17
- **THEN** the damage dice are 1d10 and 4d6 respectively (tier map), the rest unchanged

#### Scenario: needsReview fact falls back to prose
- **WHEN** a resolved component is `needsReview` (ungrounded)
- **THEN** the service returns that component's prose span instead of a computed value, with its provenance

### Requirement: MCP tool exposes resolution to chat

The MCP server SHALL expose `resolve_character_feature(heroId, feature)` returning the resolution
result, so the chat client can request a computed, cited character fact.

#### Scenario: Chat renders a cited answer
- **WHEN** the chat client calls `resolve_character_feature(hero, "breath weapon")` for the Red Dragonborn
- **THEN** it receives the structured result and the LLM can render "15-ft cone of fire, Dex save DC 15, 3d6 [PHB p.34]"

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

