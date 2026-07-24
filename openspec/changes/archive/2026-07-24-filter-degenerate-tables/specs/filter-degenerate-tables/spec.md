## ADDED Requirements

### Requirement: Degenerate tables SHALL not be emitted
The table collector SHALL NOT emit a table that has no data rows (header-only), nor a single-row grid whose cells are stat-block ability-score tokens (e.g. "STR 22 (+6)"). Genuine tables (at least two columns and at least one data row) SHALL be emitted unchanged.

#### Scenario: A header-only grid is dropped
- **WHEN** a parsed grid has columns but zero data rows
- **THEN** no CanonicalTable is emitted for it

#### Scenario: A stat-block ability line is not a table
- **WHEN** a single-row grid's cells are ability-score tokens ("STR 22 (+6)", "DEX 19 (+4)", …)
- **THEN** it is recognized as a stat-block fragment and dropped, not emitted as a table

#### Scenario: A real table is kept
- **WHEN** a grid has ≥2 columns and ≥1 data row of real values (e.g. d20 | Plane | Pool Color)
- **THEN** it is emitted as a CanonicalTable unchanged
