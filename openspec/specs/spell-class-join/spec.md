# spell-class-join Specification

## Purpose
TBD - created by archiving change spell-class-join. Update Purpose after archive.
## Requirements
### Requirement: The system SHALL resolve the spell↔class relationship from the 5etools source index

The system SHALL provide a cached `SpellClassIndex` that loads `5etools/spells/sources.json` and answers, for a class name and a spell (name + source), whether that class can cast the spell. Spell and source names SHALL be normalized (case- and punctuation-insensitive) so entity names match the 5etools keys. When the index file is absent, the index SHALL load empty and report no relationships rather than error.

#### Scenario: A known spell resolves to its classes
- **WHEN** `SpellClassIndex.ClassesFor("Fireball", "PHB")` is queried
- **THEN** it returns the casting classes for Fireball (e.g. Sorcerer, Wizard) from `sources.json`

#### Scenario: Name normalization matches despite formatting
- **WHEN** the queried spell name differs only by case/punctuation from the index key
- **THEN** it still resolves to the same class set

#### Scenario: Missing index degrades safely
- **WHEN** `sources.json` is not present
- **THEN** the index loads empty and `CanCast` returns false for all queries (no exception)

### Requirement: The entity set query SHALL support a complete castable-by-class spell filter

`list_entities` (and `GET /retrieval/entities/list`) SHALL accept a `castableByClass` filter. When set, the query SHALL return the COMPLETE set of spells that class can cast, honoring any combined spell-level/school/source filters, with the honest total-vs-returned/truncation semantics of the entity set query. The reported total SHALL be the class-filtered count, not the pre-filter spell count.

#### Scenario: castableByClass returns only that class's spells
- **WHEN** `list_entities(castableByClass="Wizard", spellLevel=3)` is called
- **THEN** the result contains only level-3 spells a Wizard can cast, and the total reflects that class-filtered count (not all level-3 spells)

#### Scenario: The set stays complete and honest
- **WHEN** more spells match than the cap
- **THEN** the result reports the true class-filtered total and the capped returned count with the truncation signal (as in entity-set-query)

#### Scenario: An unknown class yields an empty, honest set
- **WHEN** `castableByClass` names a class with no spells (or an unrecognized class)
- **THEN** the result is an empty set with total 0 — not an error

