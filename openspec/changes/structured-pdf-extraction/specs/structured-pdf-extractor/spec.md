## ADDED Requirements

### Requirement: PDF pages are segmented into ordered text blocks
The system SHALL use `DocstrumBoundingBoxes` to detect text blocks per page and `UnsupervisedReadingOrderDetector` to produce a reading-order sequence of blocks, returning a `StructuredPage` per page containing an ordered list of `PageBlock` records and a concatenated `RawText` string.

#### Scenario: Multi-column page produces correct reading order
- **WHEN** a PDF page has two text columns
- **THEN** the extractor SHALL return blocks from the left column before blocks from the right column, in top-to-bottom order within each column

#### Scenario: Single-column page returns blocks in top-to-bottom order
- **WHEN** a PDF page has a single text column
- **THEN** the extractor SHALL return blocks ordered top-to-bottom

### Requirement: Block heading level is inferred from font size
The system SHALL infer a heading level for each block by comparing the median letter font size of the block against all distinct font sizes on the page. The largest size is `h1`, the next distinct size `h2`, the next `h3`, and all remaining sizes are `body`.

#### Scenario: Three distinct font sizes produce three heading levels
- **WHEN** a page has blocks at font sizes 18pt, 14pt, and 10pt
- **THEN** blocks at 18pt are labelled `h1`, blocks at 14pt are `h2`, blocks at 10pt are `body`

#### Scenario: Single font size labels all blocks as body
- **WHEN** all blocks on a page share the same font size
- **THEN** all blocks are labelled `body`

### Requirement: RawText is the ordered concatenation of all block texts
The system SHALL set `RawText` to the block texts joined with newlines in reading order.

#### Scenario: RawText matches block concatenation
- **WHEN** a page has blocks `["Totem Warrior", "Bear", "While raging..."]`
- **THEN** `RawText` SHALL equal `"Totem Warrior\nBear\nWhile raging..."`
