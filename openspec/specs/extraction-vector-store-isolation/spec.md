# extraction-vector-store-isolation Specification

## Purpose
TBD - created by archiving change extract-srd-canonical-json. Update Purpose after archive.
## Requirements
### Requirement: Entity extraction SHALL NOT write to either Qdrant collection

`POST /admin/books/{id}/extract-entities` SHALL produce only filesystem artifacts — the canonical JSON at `books/canonical/<book-slug>.json` and its optional siblings `<book-slug>.errors.json` / `<book-slug>.warnings.json`. The extraction run SHALL NOT upsert, delete, or otherwise mutate any point in the `dnd_blocks` or `dnd_entities` Qdrant collections. Writes to `dnd_blocks` SHALL occur only via `ingest-blocks`, and writes to `dnd_entities` SHALL occur only via `ingest-entities`. A book MAY therefore be registered and extracted purely to produce its canonical JSON for review, leaving both vector collections unchanged.

#### Scenario: Extraction leaves both vector collections unchanged

- **WHEN** `POST /admin/books/{id}/extract-entities` completes successfully for a registered book on which neither `ingest-blocks` nor `ingest-entities` has been run
- **THEN** `books/canonical/<book-slug>.json` exists on disk
- **AND** the `dnd_blocks` collection point count is identical to its value before the run
- **AND** the `dnd_entities` collection point count is identical to its value before the run

#### Scenario: Ingestion is the only path to the vector collections

- **WHEN** content needs to appear in `dnd_blocks` or `dnd_entities`
- **THEN** `dnd_blocks` is populated only by `POST /admin/books/{id}/ingest-blocks`
- **AND** `dnd_entities` is populated only by `POST /admin/books/{id}/ingest-entities`
- **AND** running `extract-entities` alone never causes content to appear in either collection

