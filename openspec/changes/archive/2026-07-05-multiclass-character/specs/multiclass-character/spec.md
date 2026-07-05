## ADDED Requirements

### Requirement: Character sheet models per-class levels

`CharacterSheet` SHALL model a character's classes as a list of per-class entries (`class`, `level`, `subclass`) as the source of truth. `Class`, `Subclass`, `Level`, and `ProficiencyBonus` SHALL be DERIVED — primary class from the first entry, total level as the sum of per-class levels, proficiency bonus from the total level. This SHALL support any combination, caster or not.

#### Scenario: A multiclass character reports the correct total level

- **WHEN** a character has Rogue 3 and Fighter 2
- **THEN** the derived total level is 5, the proficiency bonus is that of a 5th-level character, and each class's individual level is retrievable

#### Scenario: A per-class feature resolves against that class's level, not the total

- **WHEN** a class feature that scales by class level is resolved for a Rogue 3 / Fighter 2 character
- **THEN** it uses the relevant class's level (e.g. Rogue 3), not the total character level

### Requirement: Existing single-class heroes migrate tolerantly

Existing single-class `HeroSnapshot` rows (whose JSON has the legacy flat `Class`/`Subclass`/`Level` and no class list) SHALL load correctly by back-filling a single-entry class list on deserialization. No data-migration script SHALL be required, and a round-trip SHALL preserve the character.

#### Scenario: Legacy single-class snapshot loads as a one-class character

- **WHEN** a pre-existing snapshot with flat `Class = "Wizard"`, `Level = 5` is loaded
- **THEN** it presents as a one-entry class list (Wizard 5) with total level 5, and re-serialising then re-loading it is unchanged

### Requirement: Multiclass validity is checked deterministically for any combination

The system SHALL determine whether a character may multiclass into or out of a class using the ability-score **prerequisites**, and SHALL report the **reduced proficiency subset** a class grants when taken as a multiclass. This SHALL apply to non-caster combinations (e.g. Rogue/Fighter) with no spellcasting involved.

#### Scenario: A prerequisite blocks an illegal multiclass

- **WHEN** a character with Dexterity 12 attempts to multiclass into Rogue (which requires Dexterity 13)
- **THEN** the check reports not-allowed with the failed prerequisite

#### Scenario: Multiclassing grants only the reduced proficiency subset

- **WHEN** a character multiclasses into Fighter
- **THEN** the granted proficiencies are the multiclass subset (e.g. light and medium armor, shields, martial weapons) and NOT the full first-class set (no heavy armor, no saving throws)

#### Scenario: A non-caster multiclass is validated without touching spellcasting

- **WHEN** validity is resolved for a Rogue 3 / Fighter 2 character
- **THEN** the result is computed purely from prerequisites and proficiency subsets, and no spellcasting composition is performed

### Requirement: Spellcasting slots compose via combined caster level

When a character has one or more spellcasting classes, spell slots SHALL be computed from a **combined caster level** — full-caster levels plus half-caster levels rounded down (Artificer rounded up) plus third-caster levels rounded down — read against the Multiclass Spellcaster slot table. Slots SHALL NOT be summed per class. Warlock Pact Magic SHALL be tracked separately from this combined pool.

#### Scenario: Combined caster level drives the slot table

- **WHEN** a character is Paladin 6 / Sorcerer 2
- **THEN** the combined caster level is ⌊6/2⌋ + 2 = 5, and spell slots are read from the Multiclass Spellcaster table at combined level 5

#### Scenario: Warlock Pact Magic is kept separate

- **WHEN** a character is Warlock 3 / Sorcerer 2
- **THEN** the combined caster level counts only the Sorcerer (2), and the Warlock's Pact Magic slots are reported separately, not merged into the combined pool

#### Scenario: The slot answer carries provenance

- **WHEN** multiclass spell slots are resolved
- **THEN** the result cites the seeded Multiclass Spellcaster table (a provenance reference), consistent with how other resolved facts cite their source

### Requirement: Spell save DC and attack are per caster class

Spell save DC and spell attack bonus SHALL be resolved per caster class, each using that class's own spellcasting ability, never as a single combined value.

#### Scenario: Each caster class reports its own save DC

- **WHEN** spell save DC is resolved for a Cleric 3 / Wizard 2 character
- **THEN** two components are returned — the Cleric's DC (Wisdom-based) and the Wizard's DC (Intelligence-based)

### Requirement: Resolution forks single-class vs multiclass and is exposed via MCP

The `CharacterResolutionService` SHALL fork spell-slot resolution on the character's class list: a single caster class resolves via that class's slot progression; a multiclass with casters resolves via the combined-caster-level path. The multiclass-aware slot resolution, per-class save DC, and multiclass-validity checks SHALL be exposed as MCP tools returning `ResolvedFact` with components and provenance.

#### Scenario: Single-class caster keeps the direct path

- **WHEN** spell slots are resolved for a single-class Wizard 5
- **THEN** the direct single-class progression is used (equivalently, combined caster level 5)

#### Scenario: MCP tool returns a structured, cited multiclass answer

- **WHEN** the MCP `resolve_spell_slots` tool is called for a multiclass caster
- **THEN** it returns a `ResolvedFact` with slot components (and a separate Warlock pact component if present) and a provenance reference
