# TABLE EXTRACTION QUALITY — INVESTIGATE THE NOISE (roadmap note, 2026-07-19)

`mineru-table-extraction` shipped and the corpus table re-extraction (all 8 official books, table_enable + preserve + collect → CanonicalTable) produced **~875 tables**. Quality check across the finished books (PHB/DMG/XGE/SCAG/MTF): **~36% of tables are DEGENERATE** (<2 columns or 0 data rows). Entities themselves are clean (100% have populated fields).

## TODO — investigate WHY there is so much table noise (before applying the fixes)
Do a proper corpus-wide breakdown of the degenerate/junk tables, not just the one cause found so far:
- **Known cause #1 (dominant):** MinerU tags a monster **stat-block ability-score line** ("STR 22 (+6)  DEX 19 (+4)  CON 24 (+7)…") as a `table` → parser makes it a 0-data-row "table". MTF (monster book) alone had 106. Captured fix = `filter-degenerate-tables` (drop 0-data-row grids + stat-block-shaped single-row grids).
- **Check for OTHER noise sources** not yet characterized: multi-column text/sidebars mis-detected as tables? spanning/merged-cell mangling? single-column "tables"? repeated/duplicate tables? tables split across page breaks into fragments? Quantify each with a corpus scan (books/canonical/*.json `tables[]`): distribution of (colCount, rowCount), share that are stat-block-shaped vs other, sample the non-stat-block degenerates.
- **Naming noise:** MinerU emits `table_caption` for only ~11% (14/129 PHB); the rest default to `Table N`. Captured fix = `table-name-from-heading` (name from preceding section_header).

## Deferred fixes already captured (openspec changes)
- `filter-degenerate-tables` — drop 0-data-row + stat-block-shaped tables.
- `table-name-from-heading` — name caption-less tables from the preceding heading.
- (broader) `extraction-content-classification`, `extraction-cross-type-recovery` — the entity-side "map, don't just decline" work.

## Sequence
After the corpus run finishes: (1) run the noise investigation above; (2) apply `filter-degenerate-tables` + `table-name-from-heading`; (3) a re-collect/re-extract gives a clean, well-named table set; (4) then wire auto-collected tables to the resolution engine (id alignment — Draconic Ancestry came out as "Table 7", won't match `phb14.table.draconic-ancestry`). Related: [[read_path_frontier]], archived `2026-07-18-mineru-table-extraction`.
