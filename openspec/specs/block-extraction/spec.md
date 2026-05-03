# block-extraction Specification

## Purpose
TBD - created by archiving change block-direct-embedding. Update Purpose after archive.
## Requirements
### Requirement: IPdfBlockExtractor produces ordered text blocks per page
The system SHALL provide an `IPdfBlockExtractor` interface with a method `ExtractBlocks(string filePath) → IEnumerable<PdfBlock>` whose default implementation uses PdfPig's `NearestNeighbourWordExtractor`, `DocstrumBoundingBoxes` page segmenter, and `UnsupervisedReadingOrderDetector` to produce an ordered sequence of blocks for every page in the PDF. Each `PdfBlock` SHALL carry `Text`, `PageNumber` (1-based), `Order` (within-page reading-order index, 0-based), and a normalised `BoundingBox` (X, Y, Width, Height in PDF user units).

#### Scenario: Multi-column page returns blocks in correct reading order
- **WHEN** `ExtractBlocks` is called against a two-column PDF page where the left column reads `"Spells\nFireball"` and the right column reads `"Cone of Cold\nLightning Bolt"`
- **THEN** the returned blocks for that page are ordered left-column-first then right-column ("Spells", "Fireball", "Cone of Cold", "Lightning Bolt"), not interleaved

#### Scenario: Block-text omits whitespace-only fragments
- **WHEN** a page contains decorative whitespace blocks (page numbers, header/footer with only digits)
- **THEN** blocks whose `Text` after trimming is empty SHALL be excluded from the returned sequence

#### Scenario: Each block carries its source page number
- **WHEN** blocks are returned for a 100-page PDF
- **THEN** the `PageNumber` field on each block matches the 1-based PDF page index it was extracted from, and `Order` is monotonically increasing within a page starting at 0

#### Scenario: PDF without text (image-only) returns no blocks
- **WHEN** the PDF page contains only image content with no extractable text
- **THEN** `ExtractBlocks` returns an empty sequence for that page without throwing

### Requirement: PdfBlock is a value record
The `PdfBlock` type SHALL be a `sealed record` with the fields `string Text`, `int PageNumber`, `int Order`, `PdfRectangle BoundingBox` and SHALL be safe to compare by value, store in collections, and serialise without custom converters.

#### Scenario: Two blocks with identical fields are equal
- **WHEN** two `PdfBlock` instances are constructed with the same `Text`, `PageNumber`, `Order`, and `BoundingBox`
- **THEN** they compare equal under `record` value equality

