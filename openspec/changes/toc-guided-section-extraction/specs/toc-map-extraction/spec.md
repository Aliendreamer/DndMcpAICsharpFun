# toc-map-extraction

## Purpose

Defines requirements for parsing a single PDF table-of-contents page via LLM and producing a structured section map used to guide entity extraction.

## Requirements

### Requirement: ITocMapExtractor parses TOC page text into a structured map
The system SHALL provide `ITocMapExtractor` with a single method `ExtractMapAsync(string tocPageText, CancellationToken)` that sends the TOC text to the LLM and returns `IReadOnlyList<TocSectionEntry>` where each entry has `Title`, `Category`, `StartPage`, and `EndPage`.

#### Scenario: Valid TOC text produces a populated map
- **WHEN** `ExtractMapAsync` is called with text from a PHB-style TOC page
- **THEN** the result contains one entry per chapter/section with non-null `Title`, valid `Category`, and `StartPage > 0`

#### Scenario: End pages are derived from adjacent start pages
- **WHEN** the LLM returns entries with sequential start pages (e.g. Warlock=105, Wizard=113)
- **THEN** Warlock's `EndPage` SHALL be 112 (next start - 1); any entry missing `EndPage` is computed in code from the next entry's `StartPage - 1`

#### Scenario: Non-game content entries are excluded
- **WHEN** the TOC contains entries like "Introduction", "Index", "Preface"
- **THEN** those entries are returned with `Category = null` and are excluded from the active section map

#### Scenario: Empty or unparseable TOC returns empty map
- **WHEN** the LLM returns invalid JSON or an empty array
- **THEN** `ExtractMapAsync` returns an empty list and logs a warning

### Requirement: TocSectionEntry carries title and page range
The `TocSectionEntry` record SHALL have: `string Title`, `ContentCategory? Category`, `int StartPage`, `int EndPage`.

#### Scenario: Entry fields are accessible
- **WHEN** a `TocSectionEntry` is constructed
- **THEN** all four fields are readable and correctly typed

### Requirement: TocCategoryMap is enhanced with title and end page
`TocCategoryMap` SHALL be updated so that `GetCategory(int page)` continues to work and a new `GetEntry(int page)` method returns the full `TocSectionEntry` for that page (or null if no entry covers it).

#### Scenario: GetEntry returns correct entry for a mid-section page
- **WHEN** `GetEntry(108)` is called and the map has Warlock covering 105–112
- **THEN** the returned entry has `Title = "Warlock"`, `Category = Class`, `StartPage = 105`, `EndPage = 112`

#### Scenario: GetEntry returns null for uncovered pages
- **WHEN** `GetEntry` is called with a page number not covered by any entry
- **THEN** it returns null
