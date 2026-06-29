## ADDED Requirements

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
