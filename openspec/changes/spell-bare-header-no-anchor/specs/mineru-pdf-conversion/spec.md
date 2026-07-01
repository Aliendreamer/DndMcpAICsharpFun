## ADDED Requirements

### Requirement: A bare spell-header block is promoted without a Casting-Time anchor

The converter SHALL promote a `text` block whose first line is a short bare spell header (a name followed
by a level/school token — `NAME <Nth-level> <school>` or `NAME <school> cantrip`) to a synthetic
`section_header` for the spell name, even when no `Casting Time` line is present. It MUST NOT promote a
spell-list row that begins with the level (no name prefix) or a long prose line that merely contains a
school word.

#### Scenario: A bare header with an OCR-dropped Casting Time is promoted
- **WHEN** a text block's first line is "GREATER RESTORATION 5th-level abjuration" with no Casting Time nearby
- **THEN** the converter emits a `section_header` "GREATER RESTORATION"

#### Scenario: A spell-list row is not promoted
- **WHEN** a text block begins "5TH LEVEL Banishing Smite Circle of Power Destructive Smite"
- **THEN** no `section_header` is emitted (the level-first row has no name prefix)

#### Scenario: A prose line is not promoted
- **WHEN** a long text line merely contains a school word mid-sentence
- **THEN** no `section_header` is emitted
