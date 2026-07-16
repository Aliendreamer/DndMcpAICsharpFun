# full-coverage-block-toc Specification

## Purpose
TBD - created by archiving change bookmarkless-block-fallback. Update Purpose after archive.
## Requirements
### Requirement: The block extractor SHALL surface section-header items alongside blocks

`IPdfBlockExtractor.ExtractBlocksAsync` SHALL return both the ordered prose blocks and the PDF's `section_header` structure items produced by the same single conversion, so a consumer can build a fallback table of contents without re-converting the PDF. The heading items SHALL preserve each header's text and 1-based page number.

#### Scenario: Extraction yields both blocks and headings from one conversion

- **WHEN** `ExtractBlocksAsync` is called against a PDF whose converted structure contains both prose items and `section_header` items
- **THEN** the result exposes the prose items as ordered blocks AND the `section_header` items as a separate headings list, each heading carrying its text and page number, produced from a single conversion

#### Scenario: A PDF with no section headers yields an empty headings list

- **WHEN** the converted structure contains prose items but no `section_header` items
- **THEN** the result's blocks are populated and its headings list is empty (not null)

### Requirement: A bookmark-less TOC SHALL cover every page with no gaps

The full-coverage heading mapper SHALL map a list of `section_header` items to `TocSectionEntry` values such that, once assembled into a `TocCategoryMap`, every page from 1 onward resolves to exactly one entry. No page within the block range SHALL resolve to a null entry.

#### Scenario: Every heading becomes a titled section boundary

- **WHEN** the mapper is given several non-empty headings on ascending pages
- **THEN** it emits one entry per heading whose `Title` is that heading's text, and consecutive entries form contiguous page ranges via `TocCategoryMap`

#### Scenario: Front matter before the first heading is covered

- **WHEN** the first heading starts on a page greater than 1
- **THEN** the mapper prepends a catch-all entry starting at page 1 so blocks on the pages before the first heading still resolve to a section (never dropped)

#### Scenario: A structureless PDF still ingests fully

- **WHEN** the mapper is given an empty heading list
- **THEN** it emits a single catch-all entry starting at page 1 that covers the whole book, so every block resolves to that one section

### Requirement: Section categories SHALL carry forward from the last confident heading

For each heading, the mapper SHALL assign the category returned by `HeadingCategoryClassifier` when that category is confident; otherwise it SHALL inherit the category of the most recent confident heading; otherwise it SHALL default to `Rule`. Every emitted entry SHALL retain its own heading text as `Title` regardless of category.

#### Scenario: Sub-headings inherit the enclosing confident category

- **WHEN** a confident `Monster` heading (e.g. "Monsters") is followed by keyword-less name headings (e.g. "Yuan-ti Anathema")
- **THEN** the name headings are emitted with their own titles but the `Monster` category carried forward from the enclosing heading

#### Scenario: Headings before any confident heading default to Rule

- **WHEN** one or more headings appear before the first confident heading
- **THEN** those entries are emitted with category `Rule` and their own titles

