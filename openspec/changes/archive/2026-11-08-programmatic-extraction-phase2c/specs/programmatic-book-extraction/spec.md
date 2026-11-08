## MODIFIED Requirements

### Requirement: Deterministic extractor service

The system SHALL provide a book extractor service, separate from the app container, that reads a book PDF and produces
Monster, Spell, MagicItem, Class, Subclass, Race, Subrace, Feat, Background, Weapon, Armor, Item, VehicleMount, Object,
Trap, DiseasePoison, Condition, God, Rule, Plane, Faction, Location and Lore entities using rules only. It MUST NOT
call any LLM and MUST NOT use a GPU. It SHALL read PDFs from a read-only mount of `books/`.

#### Scenario: Extraction without a model

- **WHEN** the extractor processes a book PDF
- **THEN** it produces entities without any network call to Ollama or another LLM service

#### Scenario: Read-only books

- **WHEN** the extractor runs
- **THEN** it cannot modify any file under `books/`

#### Scenario: Character types in one file

- **WHEN** the PHB is extracted
- **THEN** `phb14.json` holds its classes, subclasses, races, subraces, feats and backgrounds alongside its spells

#### Scenario: Equipment types in one file

- **WHEN** the PHB is extracted
- **THEN** `phb14.json` also holds its weapons, armor, adventuring gear and conditions

#### Scenario: World types in one file

- **WHEN** the PHB is extracted
- **THEN** `phb14.json` also holds its gods and named rules
