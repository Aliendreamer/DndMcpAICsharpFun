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

## NOISE INVESTIGATION — DONE (2026-07-24 corpus scan of books/canonical/*.json tables[])
Corpus is host-readable (git-crypt unlocked; books/ not sandbox-masked). Result — the noise is fully characterized and NOT a new category:
- **313/706 tables (44%) degenerate, ALL in MPMM (206/320) + MTF (106/161)** — the ONLY two books whose tables are MinerU-sourced. Every 5etools-`ProjectTables` book (DMG/ERLW/PHB/TCE/XGE, most of SCAG) = **0% degenerate**. (Monster books have no captioned 5etools reference-tables → ProjectTables projects ~0 → correctly SKIPS them → their noisy MinerU tables remain.)
- **The "other" (non-stat-block) 213 are the SAME monster-stat-block noise, mis-segmented:** MinerU splits a stat block into 1-column fragments — the "columns" cell is a run-on `"RED ABISHAI Medium Fiend (Devil)… Armor Class 22"`, rows are `"Hit Points 289…"` / bare `['STR','DEX','CON','INT','WIS','CHA']` headers (no adjacent digit → the `STR\s*\d` regex misses them). Sub-types: **283 are 1-col, 30 are 0-row.**
- **`filter-degenerate-tables` D1 (`<2col || 0-row`) drops ALL 313 by construction** (degenerate ≡ D1's condition). D2's UNIQUE marginal catch (stat-block-shaped but ≥2col & ≥1row, surviving D1) = **only 3**. So NO new filter is needed; the shipped filter fully solves it on the next MPMM/MTF re-extract (share ~65% → ~0). No other noise source exists.

## Still-open
- Wiring auto-collected tables to the resolution engine (id alignment: a collected table like "Table 7" won't match `phb14.table.draconic-ancestry`; and per the dev-flow Red Flag, id-alignment ≠ SHAPE-alignment — the resolver reads NORMALIZED columns, so a dedicated resolution projector must own the id). This concerns the CLEAN 5etools tables, NOT the MPMM/MTF stat-block noise (which belongs as Monster ENTITY fields, not tables).
- Realizing the MPMM/MTF cleanup requires a re-extract of those two books (~8.5h each; deferred).

## Sequence
After the corpus run finishes: (1) run the noise investigation above; (2) apply `filter-degenerate-tables` + `table-name-from-heading`; (3) a re-collect/re-extract gives a clean, well-named table set; (4) then wire auto-collected tables to the resolution engine (id alignment — Draconic Ancestry came out as "Table 7", won't match `phb14.table.draconic-ancestry`). Related: [[read_path_frontier]], archived `2026-07-18-mineru-table-extraction`.
