## MODIFIED Requirements

### Requirement: Results can be filtered by version, category, source book, entity name, and book type

The system SHALL apply Qdrant payload filters when `version`, `category`, `sourceBook`, `entityName`, or `bookType` query parameters are provided. `bookType` accepts case-insensitive values from the `BookType` enum (`Core`, `Supplement`, `Adventure`, `Setting`, `Unknown`); unparseable values are silently dropped.

The block-search source-book filter SHALL additionally accept a SET of source books, applied as a Qdrant OR (any-of) condition so a single query can be scoped to several source books at once (e.g. a setting's book plus the core rulebooks). A single-element set SHALL behave identically to the existing single-value source-book filter. An empty or absent set SHALL impose no source-book restriction.

#### Scenario: BookType filter limits results to a publishing class

- **WHEN** `GET /retrieval/search?q=fireball&bookType=Core` is called
- **THEN** all returned results have `metadata.bookType == Core`

#### Scenario: Filters compose

- **WHEN** `bookType=Adventure&category=Monster` is set
- **THEN** results match both filters simultaneously

#### Scenario: Version filter limits results to the specified edition

- **WHEN** `GET /retrieval/search?q=fireball&version=Edition2024` is called
- **THEN** all returned results have `metadata.version == Edition2024`

#### Scenario: Category filter limits results to the specified content type

- **WHEN** `GET /retrieval/search?q=fireball&category=Spell` is called
- **THEN** all returned results have `metadata.category == Spell`

#### Scenario: Unknown filter values are ignored

- **WHEN** `version` or `category` query parameters contain unrecognised values
- **THEN** those filters are silently dropped and the search proceeds without them

#### Scenario: A set of source books matches any of them

- **WHEN** a retrieval query supplies a source-book set of two or more books
- **THEN** results SHALL include blocks from any book in the set and exclude blocks from books not in the set
