# Tasks — filter-degenerate-tables (CAPTURE-ONLY; implement with the other table follow-ups)

## 1. Filter
- [ ] 1.1 In HtmlTableParser/MinerUTableCollector, return null / skip a table with 0 data rows (header-only).
- [ ] 1.2 Recognize a single-row grid whose cells match the stat-block ability pattern (`\b(STR|DEX|CON|INT|WIS|CHA)\b\s*\d`) as a stat-block fragment, not a table → skip.
- [ ] 1.3 Keep genuine tables (≥1 data row, ≥2 columns) unchanged.

## 2. Tests
- [ ] 2.1 0-data-row grid → dropped; STR/DEX/CON single-row grid → dropped; a real 2-col ≥1-row table kept.

## 3. Verify
- [ ] 3.1 Build clean + suite green.
- [ ] 3.2 (Optional) Re-extract a monster book (MTF) and confirm the degenerate-table share drops toward ~0.
