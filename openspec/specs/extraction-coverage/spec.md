# extraction-coverage Specification

## Purpose
TBD - created by archiving change extractor-coverage. Update Purpose after archive.

## Requirements

### Requirement: Coverage measured per book

The extractor SHALL report, for each book, the number of content lines, the number of unheld content lines, and each
unheld line with its page. A content line is an OCR line of 30 or more characters with OCR score 0.9 or higher whose
text reduced to lower-case letters and digits has 20 or more characters. It is held when that reduced text (trailing
hyphen dropped, or its digit-repaired form) is contained in the reduced text or notes of an entity of the book, or
the line was left out on purpose. A repository tool MUST compute the same measure over the committed canonical files,
counting only entities that are not `Declined`.

#### Scenario: Unclaimed paragraph counted

- **WHEN** a page holds a prose paragraph that no entity's text or notes contain and no left-out rule covers
- **THEN** each of its content lines is listed as unheld with its page, and the book's unheld count includes them

#### Scenario: Declined text is not held

- **WHEN** the only entity containing a line is `Declined`
- **THEN** the repository tool counts that line as unheld

### Requirement: Front and back matter left out on purpose

Credits, table-of-contents and index pages SHALL be recognised; their lines MUST be recorded as left out and MUST NOT
start entities.

#### Scenario: Index page

- **WHEN** a page holds ten or more lines like "Archery. See under fighting styles" or "raven, 43"
- **THEN** its lines are left out and no entity is created from it

#### Scenario: Normal prose page untouched

- **WHEN** a page holds prose with one "See chapter 5" cross-reference
- **THEN** it is not treated as an index page

### Requirement: Unclaimed prose is kept

A run of unheld prose lines that no entity claimed SHALL be kept: appended to the entity it continues when that
entity's last line directly precedes the run in reading order and the run starts lower-case or the entity's text does
not end a sentence; otherwise kept as a Lore entity flagged for review and named after the nearest preceding heading.

#### Scenario: Continuation of a section

- **WHEN** a section's text stops at the foot of a column and the next column starts "know when it is being manipulated."
  with no heading between
- **THEN** the continuation is appended to that section's entity

#### Scenario: Headingless block

- **WHEN** a page holds a paragraph that follows a stat block and starts a new sentence with no heading of its own
- **THEN** it becomes a flagged Lore named after the nearest preceding heading, not dropped

### Requirement: Drop-cap opening keeps its first letter

When an opening's first letter is set as a separate drop cap, the opening text SHALL start with that letter.

#### Scenario: Drop cap restored

- **WHEN** a chapter opens with a drop cap "W" followed by the line "hen you make your D&D character,"
- **THEN** the entity's text starts "When you make your D&D character,"

### Requirement: Coverage gate on re-extraction

An extractor change SHALL NOT raise any book's unheld count, and the corpus re-extraction that ships this change MUST
lower the corpus total. The existing keep-all gate (`--prev`, 0 newly orphaned non-quote lines) still applies.

#### Scenario: Regression blocked

- **WHEN** an extractor change makes a book's unheld count rise
- **THEN** the change is not committed until the rise is explained by a left-out rule or fixed
