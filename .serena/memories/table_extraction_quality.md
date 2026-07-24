# TABLE EXTRACTION QUALITY — INVESTIGATE THE NOISE (roadmap note, 2026-07-19)

`mineru-table-extraction` shipped and the corpus table re-extraction (all 8 official books, table_enable + preserve + collect → CanonicalTable) produced **~875 tables**. Quality check across the finished books (PHB/DMG/XGE/SCAG/MTF): **~36% of tables are DEGENERATE** (<2 columns or 0 data rows). Entities themselves are clean (100% have populated fields).

## TODO — investigate WHY there is so much table noise (before applying the fixes)
Do a proper corpus-wide breakdown of the degenerate/junk tables, not just the one cause found so far:
- **Known cause #1 (dominant):** MinerU tags a monster **stat-block ability-score line** ("STR 22 (+6)  DEX 19 (+4)  CON 24 (+7)…") as a `table` → parser makes it a 0-data-row "table". MTF (monster book) alone had 106. Captured fix = `filter-degenerate-tables` (drop 0-data-row grids + stat-block-shaped single-row grids).
- **Check for OTHER noise sources** not yet characterized: multi-column text/sidebars mis-detected as tables? spanning/merged-cell mangling? single-column "tables"? repeated/duplicate tables? tables split across page breaks into fragments? Quantify each with a corpus scan (books/canonical/*.json `tables[]`): distribution of (colCount, rowCount), share that are stat-block-shaped vs other, sample the non-stat-block degenerates.
- **Naming noise:** MinerU emits `table_caption` for only ~11% (14/129 PHB); the rest default to `Table N`. Captured fix = `table-name-from-heading` (name from preceding section_header).

## Deferred fixes — status
- `filter-degenerate-tables` — **SHIPPED 2026-07-24** (`2026-07-24-filter-degenerate-tables`, suite 1645/1645). `HtmlTableParser.Parse` drops D1 (<2-col OR 0-data-row) + D2 (stat-block fragment: ≤2-row grid, ≥3 cells matching case-sensitive `\b(STR|DEX|CON|INT|WIS|CHA)\b\s*\d`) at the single parse chokepoint; collector unchanged (already skips null). Live MTF re-extract DEFERRED (MTF tables are 5etools-`ProjectTables`-sourced, not MinerU → filter's payoff is homebrew/keyless + pre-ProjectTables; books git-crypt on host; ~8.5h run; unit tests prove D1/D2). Applies on next natural re-extraction.
- `table-name-from-heading` — **SHIPPED 2026-07-20** (`2026-07-20-table-name-from-heading`, feat `10cf2c0`): `MinerUTableCollector` already names caption-less tables from the preceding `section_header` within a 10-item window (see `MinerUTableCollector.cs:40-41`).
- (broader) `extraction-content-classification`, `extraction-cross-type-recovery` — **both SHIPPED + archived** (the entity-side "map, don't just decline" work; `automatic-decline-recovery` was the same effort, its stale active dir archived 2026-07-24).

## Still-open TODO
The "investigate WHY there's so much table noise" corpus breakdown above is STILL worth doing (other noise sources beyond stat-blocks not yet characterized), and `table-name-from-heading` + wiring auto-collected tables to the resolution engine (id alignment) remain.

## Sequence
After the corpus run finishes: (1) run the noise investigation above; (2) apply `filter-degenerate-tables` + `table-name-from-heading`; (3) a re-collect/re-extract gives a clean, well-named table set; (4) then wire auto-collected tables to the resolution engine (id alignment — Draconic Ancestry came out as "Table 7", won't match `phb14.table.draconic-ancestry`). Related: [[read_path_frontier]], archived `2026-07-18-mineru-table-extraction`.
