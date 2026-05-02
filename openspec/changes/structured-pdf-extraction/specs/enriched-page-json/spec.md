## ADDED Requirements

### Requirement: Per-page JSON carries both structured blocks and extracted entities
The system SHALL write each page extraction result as a single JSON object with fields `page` (int), `raw_text` (string), `blocks` (array of `{order, level, text}`), and `entities` (array of entity objects). The old bare-array format is not supported.

#### Scenario: Page JSON contains blocks and entities
- **WHEN** extraction completes for a page
- **THEN** the file at `extracted/<bookId>/page_<n>.json` SHALL contain a JSON object with `page`, `raw_text`, `blocks`, and `entities` fields

#### Scenario: Block array preserves reading order
- **WHEN** the structured extractor produces blocks in order `[1, 2, 3]`
- **THEN** the JSON `blocks` array SHALL contain entries with `order` values `1`, `2`, `3` in that sequence

#### Scenario: Empty page produces empty blocks and entities arrays
- **WHEN** a page yields no blocks and no entities
- **THEN** the JSON SHALL have `blocks: []` and `entities: []`

### Requirement: EntityJsonStore reads and writes only the enriched format
The system SHALL treat the new object format as the only valid format. Reading a file in the old bare-array format is not supported and will result in an empty entity list.

#### Scenario: Enriched format is parsed correctly
- **WHEN** `LoadAllPagesAsync` reads a file in the new object format
- **THEN** it SHALL return the entities from the `entities` array

#### Scenario: Old bare-array files are not parsed
- **WHEN** `LoadAllPagesAsync` reads a file containing a bare JSON array
- **THEN** it SHALL return an empty entity list for that page
