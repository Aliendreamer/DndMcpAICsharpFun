# ingestion-pipeline (delta)

## MODIFIED Requirements

### Requirement: A PDF book can be registered via the admin API
The system SHALL accept a PDF upload at `POST /admin/books/register` with form fields `sourceName`, `version`, and `displayName`, persist an ingestion record, and return HTTP 202 with the created record. The system SHALL NOT require or accept a `tocPage` form field; section boundaries are derived from the PDF's embedded bookmark tree at extraction time.

#### Scenario: Valid PDF is registered successfully
- **WHEN** a PDF file is uploaded with valid `sourceName`, `version`, and `displayName`
- **THEN** the system stores the file, creates an `IngestionRecord` with status `Pending`, and returns HTTP 202

#### Scenario: Non-PDF file is rejected
- **WHEN** a file that does not have a `.pdf` extension is uploaded
- **THEN** the system returns HTTP 400

#### Scenario: Invalid version value is rejected
- **WHEN** the `version` field does not match a valid `DndVersion` enum name
- **THEN** the system returns HTTP 400 with a message listing valid values

#### Scenario: Uploaded filename is sanitised against path traversal
- **WHEN** an upload is submitted with a filename containing directory traversal sequences
- **THEN** the system stores the file under a server-generated GUID name and the user-supplied name is retained only as a sanitised display value

## ADDED Requirements

### Requirement: Section discovery uses the PDF bookmark tree
During extraction, the system SHALL read the PDF's embedded bookmark tree (PDF outline) via `IPdfBookmarkReader.ReadBookmarks`, walk every node recursively, and convert each `(title, pageNumber)` pair to a `TocSectionEntry`. End pages SHALL be derived from each subsequent entry's start page minus one (the last entry's end page is open-ended).

#### Scenario: A bookmarked PDF produces a populated section map
- **WHEN** `ExtractBookAsync` runs against a PDF that has an embedded bookmark tree
- **THEN** the orchestrator builds a `TocCategoryMap` from the bookmarks and uses it to assign each page to a section before invoking the entity extractor

#### Scenario: A PDF without bookmarks fails extraction with a clear error
- **WHEN** `ExtractBookAsync` runs against a PDF whose bookmark tree is empty or absent
- **THEN** the orchestrator marks the record as `Failed` with an error message indicating that bookmark-driven extraction requires embedded bookmarks, and does not invoke the entity extractor

#### Scenario: Nested bookmark sub-sections are included
- **WHEN** the bookmark tree contains nested children (Chapter → Section → Subsection)
- **THEN** the recursive walker SHALL include every descendant node in the resulting `TocSectionEntry` list, not just root and immediate children

### Requirement: Bookmark titles are mapped to ContentCategory by keyword heuristic
The system SHALL provide a `BookmarkTocMapper` that converts each bookmark title to a `ContentCategory` value via case-insensitive keyword matching. Titles containing recognised keywords (e.g. `spell`, `monster`/`bestiary`/`creature`, `class`, `race`/`species`, `background`, `equipment`/`gear`/`weapons`, `condition`, `god`/`deity`, `plane`/`cosmology`, `treasure`, `encounter`, `trap`, `feat`/`trait`, `lore`/`history`) SHALL be assigned the matching category. Titles that do not match any keyword SHALL be assigned `Rule` as a permissive default to keep the section in scope rather than dropping it.

#### Scenario: Spell-related title is categorised as Spell
- **WHEN** a bookmark titled "Spell Descriptions" is mapped
- **THEN** its `ContentCategory` is `Spell`

#### Scenario: Equipment-related title is categorised as Item
- **WHEN** a bookmark titled "Adventuring Gear" or "Weapons" is mapped
- **THEN** its `ContentCategory` is `Item`

#### Scenario: Unknown title falls back to Rule
- **WHEN** a bookmark titled "Preface" or "Acknowledgements" is mapped
- **THEN** its `ContentCategory` is `Rule`

## REMOVED Requirements

### Requirement: tocPage is required at registration
**Reason**: TOC parsing has been replaced by direct PDF bookmark extraction. The user no longer needs to specify a TOC page; bookmarks provide structured section data without an LLM call.
**Migration**: Callers of `POST /admin/books/register` MUST stop sending the `tocPage` form field. The field is silently ignored if present (until the next major version, then rejected). The `TocPage` column on `IngestionRecords` is dropped via EF migration.
