# structured-entities

## Purpose

Defines the data model for structured D&D entity records: the common envelope, the deterministic slug-based ID scheme, the 20 supported entity types and their field schemas, and the canonical JSON file format used as the source of truth for entity ingestion.

## Requirements

### Requirement: Every structured entity record SHALL conform to a common envelope

The system SHALL define a common entity envelope used by every entity record across all 20 entity types. The envelope SHALL contain the following fields: `id` (string), `type` (string, one of the 20 entity types), `name` (string), `sourceBook` (string), `edition` (string, one of `Edition2014`, `Edition2024`, or other recognized editions), `page` (integer or null), `firstAppearedIn` (object with `book`, `edition`, optional `page`), `revisedIn` (array of objects each with `book`, `edition`, `summary`), `settingTags` (array of strings), `canonicalText` (string), and `fields` (object containing type-specific fields).

#### Scenario: Envelope shape is consistent across types

- **WHEN** any entity record is loaded from a canonical JSON file
- **THEN** it has all envelope fields present (`fields` may be empty for trivial types but the key SHALL exist)

#### Scenario: Missing required envelope field fails validation

- **WHEN** an entity record is loaded that lacks `id`, `type`, `name`, `sourceBook`, `edition`, `canonicalText`, or `fields`
- **THEN** the loader SHALL reject the record and surface a validation error identifying the missing field

#### Scenario: Unknown entity type fails validation

- **WHEN** a record has `type` that is not one of the 20 supported entity types
- **THEN** the loader SHALL reject the record with an error naming the unsupported type

### Requirement: Entity IDs SHALL follow a deterministic slug scheme

The system SHALL generate entity IDs as `<book-slug>.<type-slug>.<entity-slug>` where each segment is lowercase kebab-case. The same `(book, type, name)` triple SHALL always produce the same ID. IDs SHALL be globally unique within the canonical corpus.

#### Scenario: Same triple produces same ID

- **WHEN** the slug for `(Player's Handbook 2014, Class, Fighter)` is computed twice
- **THEN** both invocations return `phb14.class.fighter`

#### Scenario: Duplicate IDs across books fail load

- **WHEN** two canonical JSON files both contain a record with the same `id`
- **THEN** the loader SHALL fail with a duplicate-id error rather than silently overwriting

#### Scenario: Non-ASCII characters in names are normalised

- **WHEN** an entity name contains non-ASCII characters (e.g. accents, ligatures)
- **THEN** the slug component SHALL be ASCII-folded and kebab-cased deterministically (e.g. "Déjà Vu" → "deja-vu")

### Requirement: The system SHALL support 20 entity types

The system SHALL recognise and accept records of these types: `Class`, `Subclass`, `Race`, `Subrace`, `Background`, `Feat`, `Spell`, `Weapon`, `Armor`, `Item`, `MagicItem`, `Monster`, `Trap`, `DiseasePoison`, `VehicleMount`, `God`, `Plane`, `Faction`, `Location`, `Condition`. Each SHALL have a defined `fields` schema.

#### Scenario: Loading a Class record validates against the Class fields schema

- **WHEN** a record with `type: "Class"` is loaded
- **THEN** its `fields` block SHALL validate against the Class field schema (hitDie, primaryAbilities, savingThrowProficiencies, armorProficiencies, weaponProficiencies, toolProficiencies, skillChoices, startingEquipment, multiclass, spellcasting, subclassSelectionLevel, subclasses, asiLevels, featuresByLevel)

#### Scenario: Loading a Monster record validates against the Monster fields schema

- **WHEN** a record with `type: "Monster"` is loaded
- **THEN** its `fields` block SHALL validate against the Monster field schema (size, type, subtypes, alignment, armorClass, hitPoints, speed, abilityScores, savingThrows, skills, damageVulnerabilities, damageResistances, damageImmunities, conditionImmunities, senses, languages, challengeRating, environment, keywords, traits, actions, bonusActions, reactions, optional spellcasting, optional legendaryActions, optional lairActions, optional variantForms)

### Requirement: Class entities SHALL carry full Tier 3 progression data

Class records SHALL include a `featuresByLevel` array with exactly 20 entries (one per level 1–20). Each entry SHALL specify `level`, `proficiencyBonus`, and `features[]` where each feature has `name`, `ref` (slug-style ID for the feature, even if the feature is not yet a separate entity), and `summary` text. Caster classes SHALL have a non-null `spellcasting` block with the slot table inlined (no shared-table reference). The `multiclass` block SHALL include `prerequisites` with an `operator` field of `"and"` or `"or"` and `proficienciesGained[]`.

#### Scenario: Fighter (non-caster) record loads with spellcasting null

- **WHEN** the Fighter Class record is loaded
- **THEN** `fields.spellcasting` is `null` and `fields.featuresByLevel` has 20 entries with proficiency-bonus values that match the standard 5e progression (2,2,2,2,3,3,3,3,4,4,4,4,5,5,5,5,6,6,6,6)

#### Scenario: Wizard (full caster) record carries an inlined spell-slot table

- **WHEN** the Wizard Class record is loaded
- **THEN** `fields.spellcasting.type` is `"full"` and `fields.spellcasting.spellSlotsByLevel` is a 20-entry array (no `spellSlotsTable` shared-name reference)

#### Scenario: Paladin multiclass prerequisites use AND operator

- **WHEN** the Paladin Class record is loaded
- **THEN** `fields.multiclass.prerequisites.operator` is `"and"` and lists both Strength 13 and Charisma 13 (or the edition-appropriate values)

### Requirement: Monster entities SHALL carry both string and numeric CR

Monster records SHALL include `challengeRating` with both `cr` (string, e.g. `"1/4"`) and `crNumeric` (number, e.g. `0.25`), plus `xp` and `proficiencyBonus`. Numeric CR SHALL be sortable for filter queries.

#### Scenario: Bullywug record carries CR 1/4 with numeric 0.25

- **WHEN** the Bullywug Monster record is loaded
- **THEN** `fields.challengeRating.cr` is `"1/4"` and `fields.challengeRating.crNumeric` is `0.25`

#### Scenario: Numeric CR enables range filtering

- **WHEN** entities are queried with `crNumeric <= 3`
- **THEN** the filter returns all monsters whose `crNumeric` is at most 3 in numerically sorted order

### Requirement: Monster actions SHALL carry typed action records

Each action in a Monster's `actions[]` SHALL have a `type` field drawn from the controlled vocabulary `multiattack | melee_weapon_attack | ranged_weapon_attack | melee_or_ranged_weapon_attack | save | passive | other`. Damage SHALL be expressed as an array of `{ dice, average, type, ... }` objects to support multi-damage attacks (e.g. piercing + fire). Recharge mechanics SHALL be expressed as an optional `recharge` field on the action.

#### Scenario: Adult Red Dragon Fire Breath records type save and recharge

- **WHEN** the Adult Red Dragon Monster record is loaded
- **THEN** the Fire Breath action has `type: "save"`, `recharge: "5-6"`, and a `save` object with `ability` and `dc`

#### Scenario: Multi-damage attack stores damage as multiple entries

- **WHEN** the Adult Red Dragon Bite action is loaded
- **THEN** `damage[]` has at least two entries (piercing and fire) each with their own `dice`, `average`, and `type`

### Requirement: Provenance SHALL be captured on every entity

Every entity record SHALL include `firstAppearedIn` identifying the publication where the entity first appeared (object with `book`, `edition`, and optional `page`). Entities revised since their first appearance SHALL include a `revisedIn[]` array, each entry having `book`, `edition`, and a `summary` string describing what changed.

#### Scenario: Artificer carries first-appearance metadata

- **WHEN** the Artificer Class record is loaded from any book
- **THEN** `firstAppearedIn.book` references the original publication of the Artificer (Eberron: Rising from the Last War for the 5e Artificer)

#### Scenario: Ranger carries Tasha's revision summary

- **WHEN** the Ranger Class record from PHB 2014 is loaded
- **THEN** `revisedIn[]` includes an entry with `book: "Tasha's Cauldron of Everything"` and a `summary` describing the optional class features (Favored Foe, Deft Explorer, etc.)

### Requirement: Setting tags SHALL identify which settings an entity belongs to

Every entity SHALL include `settingTags[]`, a free-form array of lowercase setting identifiers. Setting-agnostic core content SHALL use `["core"]`. Setting-specific content SHALL use the setting slug (e.g. `["eberron"]`, `["forgotten-realms"]`, `["wildemount"]`). Entities present in multiple settings (e.g. core gods that are also Forgotten Realms) MAY use a multi-element array.

#### Scenario: Eberron-only god is tagged eberron

- **WHEN** a god record from Eberron: Rising from the Last War is loaded
- **THEN** `settingTags` contains `"eberron"` and does not contain `"core"`

#### Scenario: Core class uses the core tag

- **WHEN** the Fighter Class record from PHB 2014 is loaded
- **THEN** `settingTags` is `["core"]`

### Requirement: `canonicalText` SHALL be the embedded representation

Every entity SHALL include a `canonicalText` string. This SHALL be the text passed to the embedding model when the entity is ingested into the entity vector store. The format is per-type and SHALL be deterministic given the structured `fields` (so re-rendering an unchanged record yields identical `canonicalText`).

#### Scenario: Spell canonicalText includes header and body

- **WHEN** a Spell record is loaded
- **THEN** `canonicalText` includes the spell name, level, school, casting time, range, components, duration, full description, and `at higher levels` text

#### Scenario: Re-rendering an unchanged record yields identical canonicalText

- **WHEN** an entity record's `fields` are unchanged but the record is re-rendered through the canonical-text generator
- **THEN** the output `canonicalText` is byte-for-byte identical to the previous one

### Requirement: Canonical JSON files SHALL be versioned and validated

The system SHALL store canonical entity records in JSON files at `data/canonical/<book-slug>.json`. Each file SHALL include a top-level `schemaVersion` string. The loader SHALL reject files whose schemaVersion does not match the version supported by the running code.

#### Scenario: Mismatched schemaVersion fails load

- **WHEN** the loader reads a file with `schemaVersion: "0.5"` while the code supports `"1"`
- **THEN** the load fails with a clear schema-mismatch error

#### Scenario: Missing schemaVersion fails load

- **WHEN** the loader reads a file with no `schemaVersion` field
- **THEN** the load fails with a missing-schemaVersion error

### Requirement: Cross-entity references SHALL use entity IDs

When one entity references another (e.g. `Class.subclasses[]` listing subclass IDs, `Background.skillProfs[]` listing skill IDs, `Class.spellList` listing the slug of a SpellList entity), references SHALL be the target entity's `id` string. Dangling references (IDs with no matching entity in the loaded corpus) SHALL be reported as a validation warning at load time.

#### Scenario: Class.subclasses contains valid Subclass IDs

- **WHEN** the Fighter Class record is loaded
- **THEN** every entry in `fields.subclasses[]` is a string in the slug format `<book>.subclass.<slug>` and each refers to a Subclass record present in the corpus

#### Scenario: Dangling reference is reported

- **WHEN** a Class record references a subclass ID that does not exist in any loaded canonical JSON
- **THEN** the loader emits a warning identifying the source entity, the target ID, and the field path
