# pdf-text-extractor-tests Specification

## Purpose
TBD - created by archiving change test-coverage-wave-2. Update Purpose after archive.
## Requirements
### Requirement: PdfPigTextExtractor yields pages from a PDF
`PdfPigTextExtractor.ExtractPages` SHALL return an `IEnumerable<(int PageNumber, string Text)>` with one entry per page in the document.

#### Scenario: Single-page PDF returns one result
- **WHEN** `ExtractPages` is called with a PDF containing one page with text "Hello World"
- **THEN** exactly one tuple is returned with `PageNumber == 1` and `Text` containing "Hello" and "World"

#### Scenario: Multi-page PDF returns all pages in order
- **WHEN** `ExtractPages` is called with a three-page PDF
- **THEN** three tuples are returned with `PageNumber` values 1, 2, 3

#### Scenario: Empty PDF returns no results
- **WHEN** `ExtractPages` is called with a PDF that has no pages
- **THEN** the enumerable is empty

### Requirement: PdfPigTextExtractor logs sparse pages
`PdfPigTextExtractor` SHALL emit a Debug log entry when a page's extracted text is shorter than `MinPageCharacters`.

#### Scenario: Short page triggers sparse log
- **WHEN** `ExtractPages` is called with a page whose text is below `MinPageCharacters`
- **THEN** a Debug log message is emitted containing the filename and page number

#### Scenario: Sufficient-length page does not trigger sparse log
- **WHEN** `ExtractPages` is called with a page whose text meets or exceeds `MinPageCharacters`
- **THEN** no sparse-page log is emitted

