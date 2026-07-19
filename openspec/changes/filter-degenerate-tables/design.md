## Context
`HtmlTableParser.Parse` uses the first `<tr>` as columns and the rest as data rows. MinerU sometimes tags a single stat-block ability line as a `table`, yielding a table with columns = the ability values and 0 data rows. 36% of the corpus tables are such degenerates.

## Goals / Non-Goals
**Goals:** emit only usable tables (≥1 data row, real columns); drop stat-block ability lines. **Non-Goals:** re-parsing; the naming fix (table-name-from-heading); recovering the stat-block ability data as an entity field (it already comes via extraction/5etools).

## Decisions
- **D1** — a `CanonicalTable` with 0 data rows is dropped (header-only is not a table). **D2** — a single-row grid whose cells match `\b(STR|DEX|CON|INT|WIS|CHA)\b\s*\d` is a stat-block fragment → dropped, even if it technically has a row. **D3** — apply in the collector (has the row data) or the parser (return null); keep the ≥2-col / ≥1-row invariant for kept tables.

## Risks / Trade-offs
- A legitimate 1-row reference table could be dropped by D1 → acceptable: a 1-row "table" is a key-value line better served as prose/fields; measure how many real tables have exactly 0 data rows (expected ~none). D2 is narrow (ability-token cells only), low false-positive risk.
