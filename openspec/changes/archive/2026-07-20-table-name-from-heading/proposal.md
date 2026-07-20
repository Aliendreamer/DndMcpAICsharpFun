## Why

MinerU emits an explicit `table_caption` for only ~11% of tables (14 of 129 in PHB), so `MinerUTableCollector` falls back to `Table N` for the rest — including the flagship Draconic Ancestry table (which came out as "Table 7"). The real name almost always sits in the `section_header` block immediately preceding the table in the converted document (e.g. "ABILITY SCORE SUMMARY", "Constitution", "Draconic Ancestry"). Numeric names make tables unaddressable and break id-matching to the resolution engine (which expects e.g. `phb14.table.draconic-ancestry`).

## What Changes

- In `MinerUTableCollector`, when MinerU's `table_caption` is empty, derive the table name from the nearest preceding `section_header` structure item (within a small look-back window); only fall back to `Table N` when no heading is found. The `CanonicalTable.Id` slug then derives from the real name.

## Capabilities

### New Capabilities

- `table-name-from-heading`: auto-collected MinerU tables without a caption take their name (and id slug) from the nearest preceding section heading, so tables are named meaningfully instead of `Table N`.

### Modified Capabilities

<!-- extends mineru-table-extraction's collection step; additive -->

## Impact

- `Features/Ingestion/EntityExtraction/MinerUTableCollector.cs` (name resolution from preceding `section_header`); the collector needs the ordered item list (it already iterates `doc.Items`, so the preceding heading is available).
- Tests: a table item preceded by a `section_header` takes that name; no heading → `Table N` fallback.
- Applies on the next (re-)extraction — the in-flight corpus run's tables keep `Table N` until re-run.
