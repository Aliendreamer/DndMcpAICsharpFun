## ADDED Requirements

### Requirement: Spell stat-line mis-tagged headings are demoted to text

The converter SHALL emit a block as a `text` item, not a `section_header`, when the block is tagged as a
heading but its trimmed text begins (case-insensitive) with a spell stat label (`Casting Time:`,
`Range:`, `Components:`, `Duration:`, `Concentration`, `At Higher Levels`, `Ritual`). A legitimate
section heading that does not begin with such a label MUST be emitted unchanged.

#### Scenario: A mis-tagged Casting Time heading becomes text
- **WHEN** a heading-tagged block reads "Casting Time: 1 action"
- **THEN** the converter emits it as a `text` item (so it does not overwrite the current section title)

#### Scenario: The spell name keeps its section
- **WHEN** a `MORDENKAINEN'S SWORD` heading is followed by heading-tagged `Casting Time:` / `Range:` lines and then body text
- **THEN** the body text is attributed to the `MORDENKAINEN'S SWORD` section (the stat lines no longer overwrite it)

#### Scenario: A real heading is untouched
- **WHEN** a heading-tagged block reads "FIREBALL" or "DWARF TRAITS"
- **THEN** it is emitted unchanged as a `section_header`
