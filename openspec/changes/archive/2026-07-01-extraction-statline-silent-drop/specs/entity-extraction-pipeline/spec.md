## ADDED Requirements

### Requirement: Silent candidate loss is logged

Candidate scanning SHALL NOT lose a candidate without a trace. The system SHALL log a warning when a
section heading is overwritten by another heading before its section received any body text, and when a
scanned section is skipped because its page resolves to a null/Unknown content category. These logs MUST
NOT change extraction behavior — they only make a previously-silent loss visible.

#### Scenario: A heading overwritten with no body is logged
- **WHEN** a section heading is immediately followed by another heading and the first section received no body text
- **THEN** a warning is logged naming the dropped section title

#### Scenario: A null-category skip is logged
- **WHEN** the scanner skips a section because its page maps to a null/Unknown category
- **THEN** a warning is logged naming the skipped section and page (instead of a silent continue)
