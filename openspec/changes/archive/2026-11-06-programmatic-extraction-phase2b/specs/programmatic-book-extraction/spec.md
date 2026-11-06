## MODIFIED Requirements

### Requirement: Deterministic extractor service

The system SHALL provide a book extractor service, separate from the app container, that reads a book PDF and produces
Monster, Spell, MagicItem, Class, Subclass, Race, Subrace, Feat, Background, Weapon, Armor, Item, VehicleMount, Object,
Trap, DiseasePoison and Condition entities using rules only. It MUST NOT call any LLM and MUST NOT use a GPU. It SHALL
read PDFs from a read-only mount of `books/`.

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

### Requirement: Development scorer

The extractor package SHALL include a CLI scorer that compares a canonical file with a 5etools data directory and
prints per-field agreement, text coverage and text precision, for every type the extractor produces, including Class,
Subclass, Race, Subrace, Feat, Background, Weapon, Armor, Item, VehicleMount, Object, Trap, DiseasePoison and
Condition. The extractor service and the app MUST NOT read 5etools data.

#### Scenario: Scorer is dev-only

- **WHEN** the service processes a book
- **THEN** no 5etools file is opened

#### Scenario: Character types scored

- **WHEN** the scorer runs on `phb14.json` with `--type Class`
- **THEN** it prints found/expected and per-field agreement for the 12 PHB classes

#### Scenario: Equipment types scored

- **WHEN** the scorer runs on `phb14.json` with `--type Weapon`
- **THEN** it prints found/expected and per-field agreement for the PHB weapons
