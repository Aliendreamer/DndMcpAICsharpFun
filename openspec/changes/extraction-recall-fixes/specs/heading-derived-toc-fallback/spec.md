## ADDED Requirements

### Requirement: In-chapter pages resolve to their chapter category

A page that lies inside a content chapter SHALL resolve to that chapter's category, so that a candidate
promoted on that page is not dropped by `EntityCandidateScanner` (which skips a candidate whose page
yields a null/Unknown category). The page-category mapping SHALL cover a chapter's full page span and
SHALL account for any front-matter page offset between the converter's page indices and the table of
contents' page references.

#### Scenario: A spell deep in the alphabetical descriptions resolves to Spell
- **WHEN** a spell candidate is promoted on a page inside the spell-descriptions chapter (e.g. the "G" or "T" pages, well past the chapter's first page)
- **THEN** `TocCategoryMap.GetCategory(page)` returns the Spell category and the candidate is scanned (not dropped)

#### Scenario: Category counts outside the corrected range are unaffected
- **WHEN** the page-category mapping is corrected for the spells chapter
- **THEN** the categories of pages in other chapters (classes, races, monsters) are unchanged
