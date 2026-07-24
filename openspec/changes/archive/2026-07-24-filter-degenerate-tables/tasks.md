# Tasks — filter-degenerate-tables (CAPTURE-ONLY; implement with the other table follow-ups)

## 1. Filter
- [x] 1.1 In HtmlTableParser/MinerUTableCollector, return null / skip a table with 0 data rows (header-only). (D1 in `HtmlTableParser.Parse`: `columns.Count < 2 || rows.Count < 2` → null; collector already skips null.)
- [x] 1.2 Recognize a single-row grid whose cells match the stat-block ability pattern (`\b(STR|DEX|CON|INT|WIS|CHA)\b\s*\d`) as a stat-block fragment, not a table → skip. (D2 `IsStatBlockFragment`: ≤2-row guard, ≥3 ability-cell threshold, exact case-sensitive regex.)
- [x] 1.3 Keep genuine tables (≥1 data row, ≥2 columns) unchanged.

## 2. Tests
- [x] 2.1 0-data-row grid → dropped; STR/DEX/CON single-row grid → dropped; a real 2-col ≥1-row table kept. (5 new `HtmlTableParserTests`; RED-first confirmed; two-row stat-block test exercises D2 specifically.)

## 3. Verify
- [x] 3.1 Build clean + suite green. (build 0/0; full non-persistence suite 1645/1645; parser 12/12, collector 5/5.)
- [ ] 3.2 (Optional, DEFERRED) Re-extract a monster book (MTF) and confirm the degenerate-table share drops toward ~0. NOTE: MTF is an official 5etools book → its tables come from `ProjectTables` (5etools), not MinerU, so the filter's payoff is on the MinerU set (homebrew/keyless or pre-ProjectTables); + books are git-crypt on host (no cheap host harness) and a full re-extract is ~8.5h. Deterministic unit tests prove D1/D2. Left as a natural next-extraction confirmation.
