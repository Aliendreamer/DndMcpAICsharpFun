# entity-name-resolution Specification

## Purpose
TBD - created by archiving change extraction-name-resolution. Update Purpose after archive.
## Requirements
### Requirement: 5etools entity-name index

The system SHALL build an in-memory index from the local 5etools data (`5etools/*.json`) mapping a
normalized entity name to its canonical name and `EntityType`. The index SHALL cover TOP-LEVEL entity
types only — spell, monster, item, class, background, race, feat, condition, deity (→ God) — and SHALL
NOT index optionalfeatures or subclass-features (which are sub-features, not standalone entities).
(Planes have no standalone source array in the local 5etools mirror, so they are not indexed and are
covered by the content-first fallback.) The index SHALL be built once (singleton) at startup.

#### Scenario: Top-level entities are indexed
- **WHEN** the index is built
- **THEN** "Fireball" resolves to (canonical "Fireball", Spell), "Aboleth" to (canonical "Aboleth", Monster), and "Bard" to (canonical "Bard", Class)

#### Scenario: Sub-features are not indexed
- **WHEN** the index is built
- **THEN** an optional-feature / class-feature name like "Spellcasting" or "Archery" is NOT in the index (so it cannot be matched as a standalone entity)

#### Scenario: Monsters match across all sources
- **WHEN** a candidate "Lion" (printed in the PHB appendix but catalogued by 5etools under MM) is matched
- **THEN** it resolves to (canonical "Lion", Monster) via the full-corpus monster index, not a per-book subset

### Requirement: 5etools type mapping

The index SHALL map each 5etools type to the project `EntityType`. A 5etools `item` with a rarity
that denotes a magic item SHALL map to `MagicItem`; a mundane item SHALL map to `Item`.

#### Scenario: Magic item vs mundane item
- **WHEN** indexing 5etools items
- **THEN** "Bag of Holding" (has a rarity) maps to `MagicItem` and a mundane item (no rarity) maps to `Item`

### Requirement: Candidate-name matching with fuzzy threshold

`EntityNameMatcher.Match(rawCandidateName)` SHALL normalize the raw heading (case/punctuation) and
return the canonical name + `EntityType` of the best index match, or null. It SHALL try an exact
normalized match first, then a fuzzy match accepted only above a confidence threshold (so OCR-merged
headings resolve, but a wrong neighbour does not). A null result SHALL NOT drop the candidate.

#### Scenario: All-caps heading matches exactly
- **WHEN** `Match("FIREBALL")` is called
- **THEN** it returns (canonical "Fireball", Spell)

#### Scenario: OCR-merged heading matches by fuzzy threshold
- **WHEN** `Match("MAGEARMOR")` is called (OCR dropped the space)
- **THEN** it returns (canonical "Mage Armor", Spell) because the fuzzy match is above the confidence threshold

#### Scenario: Non-entity heading does not match
- **WHEN** `Match("ACTIONS")` or `Match("A RED DRAGON'S LAIR")` is called
- **THEN** it returns null (no index entry within the threshold)

#### Scenario: Ambiguous/low-confidence does not match
- **WHEN** a raw heading is below the fuzzy confidence threshold for every index entry
- **THEN** `Match` returns null (the candidate is NOT given a wrong canonical name — it falls through to content-first)

