## ADDED Requirements

### Requirement: Single-block spell stat headers are split into a name heading

The converter SHALL recover a spell whose entire stat header (name, level/school, and `Casting Time`) is
a single multi-line `text` block by promoting the spell name — the first line's text before the first
level/school marker — to a synthetic `section_header`. It MUST NOT split a plain prose block that merely
mentions "casting time" without a leading name and level/school token.

#### Scenario: A newline-joined stat header is split
- **WHEN** a text block is "CLOUD OF DAGGERS\n2nd-level conjuration\nCasting Time: 1 action\nRange: 60 feet"
- **THEN** the converter emits a `section_header` "CLOUD OF DAGGERS" before the block's text

#### Scenario: A space-glued stat header is split
- **WHEN** a text block is "DISGUISE SELF 1st-level illusion Casting Time: 1 action Range: Self"
- **THEN** the converter emits a `section_header` "DISGUISE SELF"

#### Scenario: A prose block is not split
- **WHEN** a text block mentions "casting time" mid-sentence with no leading name + level/school
- **THEN** no `section_header` is emitted for it
