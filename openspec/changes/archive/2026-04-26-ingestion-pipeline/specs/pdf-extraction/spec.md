## ADDED Requirements

### Requirement: Text is extracted from PDF text layer using PdfPig
The system SHALL extract text from each page of a PDF using PdfPig's text extraction, producing a sequence of page text strings with their page numbers.

#### Scenario: Text-layer PDF is extracted successfully
- **WHEN** a PDF with an embedded text layer is processed
- **THEN** each page yields a non-empty string of extracted text and a page number

#### Scenario: Page with no text yields empty string, not an error
- **WHEN** a PDF page contains only images or is blank
- **THEN** the extractor returns an empty string for that page without throwing

### Requirement: Extraction attempts two-column layout reconstruction
The system SHALL use PdfPig's `TopToBottomLeftToRight` reading order to partially reconstruct two-column page layouts common in D&D books.

#### Scenario: Two-column page text is extracted in column order
- **WHEN** a page has two visible text columns
- **THEN** the extracted text reads left column top-to-bottom before right column top-to-bottom

### Requirement: Extraction warnings are logged
The system SHALL log a warning for each page where text extraction produces fewer than a configured minimum character count, indicating potential layout or scan issues.

#### Scenario: Sparse page triggers warning
- **WHEN** a page yields fewer than `IngestionOptions:MinPageCharacters` characters
- **THEN** a structured log warning is emitted with the book name and page number
