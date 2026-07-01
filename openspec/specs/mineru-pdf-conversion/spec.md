# mineru-pdf-conversion Specification

## Purpose
TBD - created by archiving change mineru-main-parser. Update Purpose after archive.
## Requirements
### Requirement: MinerU is the sole PDF structure converter

The system SHALL register the MinerU-based converter as the only `IPdfStructureConverter`. Marker
SHALL be removed entirely — there is no fallback converter and no enable/disable flag.

#### Scenario: MinerU is the registered converter
- **WHEN** the host starts
- **THEN** the registered `IPdfStructureConverter` is the MinerU converter (no Marker type is registered)

### Requirement: Automatic MinerU conversion via a service

The system SHALL convert a PDF by submitting it to a MinerU conversion service (no manual CLI step),
using the `pipeline` backend and the `ocr` method, and SHALL map the service's structured output onto
`PdfStructureDocument` (blocks carrying a heading level → `section_header`; plain text → `text`;
headers/footers/page-numbers/images/tables dropped).

#### Scenario: A registered book is converted automatically
- **WHEN** entity extraction runs for a book and no cached conversion exists
- **THEN** the MinerU service is invoked for that PDF and its output is mapped to `PdfStructureDocument`

#### Scenario: OCR method is used
- **WHEN** the converter requests a MinerU conversion
- **THEN** it requests the `pipeline` backend with the `ocr` method (so corrupt embedded text is bypassed)

### Requirement: Spell-chapter splitter recovers spell entries

The MinerU converter SHALL recover spell entries whose names are not tagged as headings by anchoring on
`Casting Time:` blocks: when a block contains "Casting Time" and the preceding block is a level/school
line, the system SHALL promote the spell name (the text before the first digit or school word) to a
synthetic `section_header`.

#### Scenario: A spell name that is not a heading becomes a candidate
- **WHEN** a level/school line (e.g. "ALTER SELF 2nd-level transmutation") is immediately followed by a "Casting Time:" block, and neither is tagged as a heading
- **THEN** the converter emits a `section_header` for the spell name ("ALTER SELF")

### Requirement: MinerU conversions are cached

The system SHALL cache each MinerU conversion on disk keyed by the PDF content, and SHALL reuse the
cached structure on subsequent conversions of the same PDF, so the OCR step is paid at most once per
book.

#### Scenario: Re-extraction reuses the cached conversion
- **WHEN** a book is re-extracted (`force=true`) and a cached MinerU conversion exists for its PDF
- **THEN** the cached structure is used and the MinerU service is not re-invoked

### Requirement: Spell-name headings are cleaned of level/school

The converter SHALL emit the cleaned spell name (the text before the first level/school marker) when
MinerU tags a spell entry's name as a heading with the level/school glued onto it, rather than the raw
heading text. Ordinary section headings that carry no level/school token MUST be emitted unchanged.

#### Scenario: A spell heading with a glued school is cleaned
- **WHEN** a heading-tagged block reads "PRESTIDIGITATIONTransmutation cantrip"
- **THEN** the converter emits a `section_header` of "PRESTIDIGITATION" (the school/level suffix stripped)

#### Scenario: A heading with a glued OCR level is cleaned
- **WHEN** a heading-tagged block reads "GUIDING BOLT Ist-level evocation"
- **THEN** the converter emits a `section_header` of "GUIDING BOLT"

#### Scenario: An ordinary heading is untouched
- **WHEN** a heading-tagged block reads "PART 3 | SPELLS" (no level/school token)
- **THEN** the converter emits it unchanged

### Requirement: Race-section fallback recovers untagged race titles

The converter SHALL recover a race whose bare section title was not tagged as a heading by anchoring on
its traits heading: when a short heading ends with " TRAITS", the converter SHALL promote the preceding
word(s) as a synthetic race-name `section_header`, deduplicated against an already-emitted heading of the
same name.

#### Scenario: A race with no bare title is recovered from its TRAITS heading
- **WHEN** "GNOME TRAITS" is tagged as a heading but no bare "GNOME" heading precedes it
- **THEN** the converter emits a `section_header` of "GNOME"

#### Scenario: An already-tagged race is not duplicated
- **WHEN** a bare "DWARF" heading already precedes "DWARF TRAITS"
- **THEN** no second "DWARF" `section_header` is emitted from the TRAITS heading

### Requirement: Spell-name extraction preserves a school word in the name

The converter SHALL extract a promoted spell name by stripping only the trailing level/school suffix
(`"<Nth-level> <school>"` or `"<school> cantrip"`), so a spell whose name itself contains a school word
is preserved. It MUST NOT cut the name at the first school word when that word is part of the name.

#### Scenario: A cantrip whose name ends in a school word
- **WHEN** the promoted block is "MINOR ILLUSION Illusion cantrip"
- **THEN** the extracted name is "MINOR ILLUSION" (only the trailing "Illusion cantrip" suffix removed)

#### Scenario: Another school-word-name cantrip
- **WHEN** the promoted block is "PROGRAMMED ILLUSION Illusion ..." with an illusion/cantrip suffix
- **THEN** the extracted name is "PROGRAMMED ILLUSION", not "PROGRAMMED"

#### Scenario: A leveled spell is cut at the digit (unchanged)
- **WHEN** the promoted block is "SHIELD OF FAITH 1st-level abjuration"
- **THEN** the extracted name is "SHIELD OF FAITH" (cut at the level digit)

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

