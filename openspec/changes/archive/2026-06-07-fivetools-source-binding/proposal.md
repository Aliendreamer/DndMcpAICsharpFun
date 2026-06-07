## Why

Our ingested books (PDFs) have no link to 5etools source identifiers (e.g. `PHB`, `XDMG`). This means entities extracted from our PDFs use display names like `"Dungeon Master's Guide"` as their `sourceBook`, while 5etools-ingested entities use short codes like `"DMG"` — so unified search, edition filtering, and MCP intent aliases (e.g. "core books", "2024 rules") cannot work across both pipelines.

## What Changes

- Add nullable `FivetoolsSourceKey` column to `IngestionRecord` (e.g. `"XDMG"`, `"PHB"`)
- New `BookSourceRegistry` singleton service reads `5etools/books.json` at startup — provides group, published year, display abbreviation (`DMG'24`) for any source key
- `POST /admin/books/register` accepts optional `fivetoolsSourceKey` form field; `GET /admin/5etools/sources` returns the valid source list with suggestions based on display name similarity
- Canonical entity extraction writes `source` field from the book's registered source key (not the PDF display name)
- Qdrant entity payload gains `sourceBook` normalised to the source key across both pipelines
- Entity schema gains `srd`, `srd52`, `basicRules2024` boolean flags — populated from 5etools JSON for 5etools-ingested entities, defaulting to `false` for extracted entities
- MCP-facing intent aliases resolve group/year/SRD intents to source key lists at query time
- **No changes** to any file under `5etools/`

## Capabilities

### New Capabilities

- `book-source-registry`: Registry service that loads `5etools/books.json` and resolves source keys to metadata (group, year, display abbreviation) and group/intent aliases to source key lists
- `fivetools-source-key`: Binding of an `IngestionRecord` to a 5etools source identifier, propagated through extraction and into Qdrant payloads
- `srd-availability-flags`: Per-entity boolean flags (`srd`, `srd52`, `basicRules2024`) in canonical JSON and Qdrant payload

### Modified Capabilities

- `ingestion-pipeline`: Registration endpoint gains optional `fivetoolsSourceKey`; extraction writes normalised `source` field from the registered key
- `llm-extraction`: Extraction output writes `source` from book's `FivetoolsSourceKey` rather than leaving it empty

## Impact

- **DB**: one new nullable column + migration on `IngestionRecords`
- **API**: `POST /admin/books/register` (new optional field), new `GET /admin/5etools/sources` endpoint
- **Qdrant**: `dnd_entities` payload gains `srd`, `srd52`, `basicRules2024` keyword fields; `sourceBook` normalised
- **Canonical JSON schema**: `source`, `srd`, `srd52`, `basicRules2024` added to entity objects
- **MCP tools**: intent resolver added for group/year/SRD filtering
