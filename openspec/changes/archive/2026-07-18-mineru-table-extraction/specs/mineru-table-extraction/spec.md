## ADDED Requirements

### Requirement: MinerU-recognized tables SHALL be preserved through conversion

The MinerU converter SHALL read MinerU's `table_body` (HTML) and `table_caption` and emit a `table` structure item carrying that HTML, its caption, and its page — instead of dropping table blocks. Image/equation/header/footer blocks MAY still be dropped.

#### Scenario: A table block becomes a table structure item
- **WHEN** MinerU's `content_list` contains a `{ "type":"table", "table_body":"<table>…</table>", "page_idx":N }` block
- **THEN** the converted document contains a structure item of type `table` carrying the table HTML and page N (the block is not dropped)

#### Scenario: Non-table decorative blocks are still dropped
- **WHEN** a block is an image, equation, header, footer, or page number
- **THEN** it is not emitted as a structure item (unchanged behavior)

### Requirement: A table's HTML SHALL be parsed deterministically into a CanonicalTable

A deterministic parser SHALL turn a MinerU table's HTML into a `CanonicalTable` — named columns plus rows of cells — with no LLM. The first row supplies the column names; subsequent rows become `CanonicalTableRow`s of `CanonicalCell`s. Whitespace/entities SHALL be normalized (OCR-tolerant). Malformed or empty HTML SHALL yield no table (skipped, not thrown).

#### Scenario: A well-formed table parses to columns and rows
- **WHEN** the parser is given the Draconic Ancestry table HTML (header `Dragon | Damage Type | Breath Weapon`; rows `Black | Acid | 5 by 30 ft. line (Dex. save)`, `Blue | Lightning | …`)
- **THEN** it returns a `CanonicalTable` whose columns are `[Dragon, Damage Type, Breath Weapon]` and whose rows carry those cell values in order

#### Scenario: Malformed HTML is skipped safely
- **WHEN** the parser is given empty or malformed table HTML
- **THEN** it returns no table and does not throw

### Requirement: Parsed tables SHALL carry provenance and reach the canonical JSON

Each parsed `CanonicalTable` SHALL carry provenance (source book + the table's page) on its cells, and SHALL be added to `CanonicalJsonFile.Tables` during extraction so the existing writer serializes it and the existing projector lands it in Postgres on ingest.

#### Scenario: A converted book's tables appear in the canonical file
- **WHEN** a book is extracted and its converted document contains table items
- **THEN** the produced `CanonicalJsonFile.Tables` contains a `CanonicalTable` per parsed table, each cell bearing a `ProvenanceRef` with the source book and the table's page

#### Scenario: Tables flow to Postgres via the existing projector
- **WHEN** the canonical JSON with populated `Tables` is ingested
- **THEN** the existing `StructuredFactProjector` writes the tables/rows to Postgres (no new projection code is required by this change)
