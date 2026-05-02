# section-level-extraction

## Purpose

Defines requirements for grouping page blocks into focused sections and extracting one entity per section with TOC-derived context hints.

## Requirements

### Requirement: Page blocks are grouped into sections by heading
The system SHALL group a page's `IReadOnlyList<PageBlock>` into sections where each section begins at an `h1` or `h2` block and includes all consecutive body and `h3` blocks until the next `h1`/`h2` or end of page. Pages with no headings produce a single section containing all blocks.

#### Scenario: Page with multiple h2 headings produces multiple sections
- **WHEN** a page has blocks [h2 "Darkvision", body, body, h2 "Hellish Resistance", body]
- **THEN** two sections are produced: one starting at "Darkvision" and one at "Hellish Resistance"

#### Scenario: Page with no headings produces one section
- **WHEN** a page has only body blocks
- **THEN** one section is produced containing all blocks

#### Scenario: h3 blocks are included in the current section
- **WHEN** an h3 block appears between two h2 blocks
- **THEN** the h3 and its following body blocks are part of the section started by the preceding h2

### Requirement: Each section is extracted with entity context hint
The system SHALL pass `entityName` (from `TocSectionEntry.Title`), `category`, `startPage`, and `endPage` to `ILlmEntityExtractor.ExtractAsync` so the prompt includes: "This is a section from the {entityName} {category} (pages {startPage}–{endPage})."

#### Scenario: Extraction prompt includes entity context
- **WHEN** extracting a section from the Warlock chapter (pages 105–112)
- **THEN** the LLM prompt contains "Warlock", "Class", "105", and "112"

#### Scenario: Section without a heading uses parent entity name
- **WHEN** a body-only section is extracted from the Warlock chapter
- **THEN** the prompt still includes "Warlock" and "Class" from the TOC map entry

### Requirement: Section extraction replaces whole-page extraction for mapped pages
For pages covered by the TOC map, the orchestrator SHALL iterate sections instead of sending the full page text. Pages not covered by the TOC map SHALL be skipped (no extraction call).

#### Scenario: Mapped page produces one extraction call per section
- **WHEN** a page has 3 heading sections and is covered by the TOC map
- **THEN** exactly 3 extraction calls are made, one per section

#### Scenario: Unmapped page is skipped
- **WHEN** a page is not covered by any TOC map entry
- **THEN** no extraction call is made for that page
