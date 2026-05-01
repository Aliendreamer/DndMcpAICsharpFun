## ADDED Requirements

### Requirement: PDF bookmark outline is read at extraction start
The system SHALL read the embedded bookmark outline from the PDF file using PdfPig at the beginning of `ExtractBookAsync`, before any per-page LLM calls are made.

#### Scenario: Bookmarks are present and read
- **WHEN** `ExtractBookAsync` is called for a book whose PDF contains an embedded outline
- **THEN** the system retrieves a non-empty list of bookmark titles with their destination page numbers

#### Scenario: No bookmarks present falls back gracefully
- **WHEN** `ExtractBookAsync` is called for a book whose PDF has no embedded outline
- **THEN** the system logs a warning and proceeds with all-categories extraction as before

### Requirement: LLM classifies bookmark titles into ContentCategory page ranges
The system SHALL call the LLM once per extraction run, passing the bookmark title list, and receive back a mapping of page ranges to `ContentCategory` values.

#### Scenario: LLM returns valid category map
- **WHEN** the bookmark list is sent to the LLM for classification
- **THEN** the system receives a JSON object mapping page ranges to `ContentCategory` enum values

#### Scenario: Bookmark title with no matching category is excluded
- **WHEN** a bookmark title (e.g. "Introduction", "Preface") does not correspond to any `ContentCategory`
- **THEN** that page range is mapped to null and pages within it are skipped entirely during extraction

#### Scenario: LLM returns invalid or unparseable JSON
- **WHEN** the LLM response cannot be parsed as a valid category map
- **THEN** the system logs a warning and falls back to all-categories extraction

### Requirement: Per-page extraction dispatches only the mapped category
The system SHALL look up each page number in the TOC category map and run at most one extractor pass per page.

#### Scenario: Page within a mapped category range runs one pass
- **WHEN** a page falls within a range mapped to `ContentCategory.Spell`
- **THEN** only the Spell extractor pass is executed for that page

#### Scenario: Page outside any mapped range is skipped
- **WHEN** a page falls within a range mapped to null (e.g. introduction pages)
- **THEN** no extractor passes run and no JSON file is written for that page
