# heading-derived-toc-fallback Specification

## Purpose
TBD - created by archiving change heading-derived-toc-fallback. Update Purpose after archive.
## Requirements
### Requirement: Bookmark-less PDFs SHALL derive their TOC from heading structure items

When a book PDF has no embedded bookmarks (the bookmark-derived `TocCategoryMap` is empty), the extraction pipeline SHALL build the `TocCategoryMap` from Marker's `section_header` structure items instead of leaving it empty. Each heading's category SHALL be determined by the same deterministic keyword classifier used for bookmark titles — no LLM is invoked for category recognition.

#### Scenario: A bookmark-less book produces typed candidates

- **WHEN** entity extraction runs on a PDF that has no embedded bookmarks but whose Marker output contains `section_header` items with category-bearing titles (e.g. "Races", "Barbarian", "Spells")
- **THEN** the candidate scanner produces entity candidates whose types derive from those headings
- **AND** the extraction does not return zero candidates solely because bookmarks are absent

#### Scenario: No bookmarks and no usable headings yields a clean empty result

- **WHEN** a PDF has neither embedded bookmarks nor any `section_header` whose title resolves to a confident category
- **THEN** extraction completes with zero candidates and logs the genuine no-signal outcome
- **AND** the run does not throw

### Requirement: Heading-derived TOC entries SHALL be sparse and confident-only

The heading-derived TOC mapper SHALL emit a `TocSectionEntry` only for headings whose title resolves to a confident category — a `ContentCategory` that maps to a non-null `EntityType` (Spell, Monster, Class, Race, Background, Item, Condition, God, Plane, Treasure, Trap). Headings that classify to a non-entity category (e.g. `Rule`, `Trait`, `Lore`) or to `Unknown`, and headings with blank titles, SHALL be omitted so they do not reset the surrounding page-range category.

#### Scenario: Keyword-less sub-headings do not reset the range

- **WHEN** a confident heading "Barbarian" (Class) is followed by keyword-less sub-headings such as "Rage", "Reckless Attack", and "Hill Dwarf" before the next confident heading
- **THEN** no TOC entry is emitted for those sub-headings
- **AND** their pages inherit the `Class` category via the existing `TocCategoryMap` page-range propagation until the next confident heading

#### Scenario: Confident categories open new ranges

- **WHEN** the heading stream transitions from a "Barbarian" (Class) heading to a later "Spells" (Spell) heading
- **THEN** pages from the "Spells" heading onward resolve to `Spell` until the next confident heading

### Requirement: The bookmark path SHALL be unaffected by the fallback

The heading-derived fallback SHALL fire only when the bookmark-derived `TocCategoryMap` is empty. For any PDF that has embedded bookmarks, the derived TOC SHALL be identical to today's bookmark-only behavior, and the shared keyword classifier SHALL produce the same category for a given title as the prior `BookmarkTocMapper` logic.

#### Scenario: A bookmarked book's TOC is unchanged

- **WHEN** entity extraction runs on a PDF that has embedded bookmarks
- **THEN** the `TocCategoryMap` is built solely from those bookmarks
- **AND** the heading-derived fallback is not invoked
- **AND** the resulting categories match the pre-change bookmark behavior

