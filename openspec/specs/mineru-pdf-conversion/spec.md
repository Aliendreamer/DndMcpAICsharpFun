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

