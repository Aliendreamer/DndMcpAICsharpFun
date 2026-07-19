## Why

The corpus table extraction (mineru-table-extraction) captured 873 tables, but ~36% (317) are **degenerate** — < 2 columns or 0 data rows. The dominant cause: MinerU tags a monster **stat-block ability-score line** ("STR 22 (+6)  DEX 19 (+4)  CON 24 (+7) …") as a table, and `HtmlTableParser` turns that single row into "columns" with 0 data rows. MTF (a monster book) alone has 106 such. These are noise — a header-only or single-stat-line grid is not a usable table — and they clutter the structured table set and Postgres projection.

## What Changes

- Drop degenerate tables at collection: a `CanonicalTable` with **0 data rows** (header-only) is not emitted; and a single-row grid whose cells are stat-block ability tokens (`STR 22 (+6)` shape) is recognized as a stat-block fragment, not a table.
- Keep all genuine tables (≥1 data row, real columns) unchanged.

## Capabilities

### New Capabilities

- `filter-degenerate-tables`: the table collector emits only usable tables — dropping header-only (0-data-row) grids and stat-block ability lines MinerU mis-tags as tables — so the structured table set is clean.

### Modified Capabilities

<!-- extends mineru-table-extraction's collection/parse; additive filter -->

## Impact

- `Features/Ingestion/Pdf/HtmlTableParser.cs` and/or `Features/Ingestion/EntityExtraction/MinerUTableCollector.cs` — skip 0-data-row tables and stat-block-shaped single-row grids.
- Tests: 0-row grid → dropped; STR/DEX/CON single-row grid → dropped; a real ≥1-row table kept.
- Applies on next re-extraction; the current corpus keeps the degenerate ~36% until re-run (or a cleanup pass).
