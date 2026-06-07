# entity-id-unification Specification

## Purpose
TBD - created by archiving change entity-data-unification. Update Purpose after archive.
## Requirements
### Requirement: Source key aliases in BookOverrides
`EntityIdSlug.BookOverrides` SHALL contain entries for both display names AND 5etools source keys, mapping to the same slug. Source key entries use the lowercase source key as both key and — where needed — a year-suffixed slug to distinguish editions.

Required mappings (source key → slug):

- `"PHB"` → `"phb14"`
- `"XPHB"` → `"phb24"`
- `"DMG"` → `"dmg14"`
- `"XDMG"` → `"dmg24"`
- `"MM"` → `"mm14"`
- `"MM25"` → `"mm24"`
- `"TCE"` → `"tce"`
- `"XGTE"` → `"xgte"`
- `"MPMM"` → `"mpmm"`
- `"VGM"` → `"vgm"`
- `"ERLW"` → `"erlw"`

#### Scenario: Source key produces same slug as display name

- **WHEN** `EntityIdSlug.For("TCE", EntityType.Subclass, "Circle of Spores")` is called
- **THEN** the result SHALL be `"tce.subclass.circle-of-spores"`

#### Scenario: Display name still works

- **WHEN** `EntityIdSlug.For("Tasha's Cauldron of Everything", EntityType.Subclass, "Circle of Spores")` is called
- **THEN** the result SHALL also be `"tce.subclass.circle-of-spores"`

#### Scenario: Edition-suffixed core books

- **WHEN** `EntityIdSlug.For("PHB", EntityType.Class, "Fighter")` is called
- **THEN** the result SHALL be `"phb14.class.fighter"`

### Requirement: Deterministic Qdrant point UUID
Every entity point written to Qdrant SHALL use a UUID derived deterministically from the entity's `id` string using UUID version 5 (SHA-1, DNS namespace). The same entity ID always produces the same Qdrant point UUID.

#### Scenario: Same entity ID produces same UUID across runs

- **WHEN** `ingest-entities` is run twice for the same book
- **THEN** each entity SHALL produce the same Qdrant point UUID both times, and the second run SHALL overwrite the first without creating duplicates

#### Scenario: Different entity IDs produce different UUIDs

- **WHEN** two entities have different `id` strings
- **THEN** their Qdrant point UUIDs SHALL be different

