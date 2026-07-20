# table-name-from-heading Specification

## Purpose
TBD - created by archiving change table-name-from-heading. Update Purpose after archive.
## Requirements
### Requirement: A caption-less table SHALL be named from its preceding heading
When MinerU emits a table with no caption, the collector SHALL name the resulting `CanonicalTable` (and derive its id slug) from the nearest preceding `section_header` within a bounded look-back window, falling back to a positional `Table N` only when no heading is found. A table WITH a MinerU caption keeps it.

#### Scenario: An uncaptioned table takes the preceding heading
- **WHEN** a `table` item with an empty caption is preceded by a `section_header` "Draconic Ancestry"
- **THEN** the CanonicalTable is named "Draconic Ancestry" and its id slug reflects that, not "Table 7"

#### Scenario: A captioned table is unchanged
- **WHEN** a table item carries a MinerU caption
- **THEN** that caption is used as the name (no heading lookup)

#### Scenario: No heading falls back to positional
- **WHEN** an uncaptioned table has no preceding heading in the window
- **THEN** it falls back to a positional `Table N` name

