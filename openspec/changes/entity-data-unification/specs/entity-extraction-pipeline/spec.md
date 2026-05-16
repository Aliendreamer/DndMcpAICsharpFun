## MODIFIED Requirements

### Requirement: LLM extraction produces correct entity type
The extraction system prompt SHALL instruct the LLM to classify each extracted entity with the correct `EntityType` based on the content it reads — `Class`, `Subclass`, `Spell`, `Monster`, `Feat`, `Item`, `MagicItem`, `Race`, `Subrace`, `Background`, `Rule`, `God`, `Condition`, `DiseasePoison`, `Weapon`, `Armor`, `Trap`, `VehicleMount` — rather than defaulting all entities to `Class`.

The prompt SHALL include:

- The full list of valid `EntityType` values
- Examples of how to classify common D&D content (subclass features → `Subclass`, spell entries → `Spell`, etc.)
- The rule: if uncertain, prefer the most specific applicable type over `Class`

#### Scenario: Subclass correctly typed

- **WHEN** the LLM extracts an entity for "Circle of Spores" from Tasha's Cauldron of Everything
- **THEN** the extracted entity SHALL have `type: "Subclass"` not `type: "Class"`

#### Scenario: Rule entry correctly typed

- **WHEN** the LLM extracts an entity for "Transmuted Spell" (a metamagic option)
- **THEN** the extracted entity SHALL have `type: "Rule"` not `type: "Class"`

#### Scenario: Unknown content falls back gracefully

- **WHEN** the LLM cannot determine a specific type for an entity
- **THEN** the LLM SHALL use `Class` as the fallback and include a note in `canonicalText`
