# docling-pdf-extraction

## ADDED Requirements

### Requirement: IDoclingPdfConverter calls docling-serve to convert PDFs
The system SHALL provide an `IDoclingPdfConverter` interface with a single method `Task<DoclingDocument> ConvertAsync(string filePath, CancellationToken ct = default)`. The default implementation `DoclingPdfConverter` SHALL POST the file as multipart/form-data to docling-serve's conversion endpoint at `{Docling:BaseUrl}/v1alpha/convert/file`, await the JSON response, and deserialise it into a strongly-typed `DoclingDocument` containing the source filename, the rendered markdown body, and an ordered list of structural items.

#### Scenario: A bookmarked PDF round-trips through docling-serve
- **WHEN** `ConvertAsync` is called with a path to a real PDF and docling-serve is healthy
- **THEN** the method returns a `DoclingDocument` whose `Items` list is non-empty and whose item ordering reflects the page reading order

#### Scenario: docling-serve returns a non-2xx response
- **WHEN** docling-serve responds with HTTP 5xx or 4xx for the conversion request
- **THEN** `ConvertAsync` throws an `InvalidOperationException` whose message includes the status code and any error body returned by docling-serve

#### Scenario: docling-serve is unreachable
- **WHEN** the HTTP request to docling-serve fails with a connection error or times out beyond `Docling:RequestTimeoutSeconds`
- **THEN** `ConvertAsync` propagates the underlying exception (`HttpRequestException` or `TaskCanceledException`)

### Requirement: DoclingDocument carries page-aware structural items
The `DoclingDocument` record SHALL include a `Markdown` string for inspection/logging and an `IReadOnlyList<DoclingItem> Items` field. Each `DoclingItem` SHALL include `Type` (one of `heading`, `paragraph`, `list_item`, `table`, `caption`, `footer`, or future Docling types as a string), `Text` (the item's flat text content), `PageNumber` (1-based), and optionally `Level` (for headings).

#### Scenario: Items have stable page numbers
- **WHEN** `ConvertAsync` returns a `DoclingDocument` for a multi-page PDF
- **THEN** every `Item` has a `PageNumber` between 1 and the PDF's page count, and items from the same page appear consecutively in the list

#### Scenario: Heading items carry a level
- **WHEN** Docling identifies a heading at level N
- **THEN** the corresponding `DoclingItem` has `Type == "heading"` and `Level == N`

### Requirement: DoclingBlockExtractor adapts Docling items to PdfBlock
The system SHALL provide `DoclingBlockExtractor : IPdfBlockExtractor` whose `ExtractBlocks(filePath)` calls `IDoclingPdfConverter.ConvertAsync` and yields one `PdfBlock` per `DoclingItem`, preserving page number and assigning a monotonic `Order` index per page. Items whose `Text` after trim is empty SHALL be skipped. The downstream `BlockIngestionOrchestrator` filters (minimum 40 chars, less than 40% letters) continue to apply unchanged.

#### Scenario: Each item produces one block
- **WHEN** Docling returns 12 items across 3 pages
- **THEN** the extractor yields 12 `PdfBlock` records (modulo whitespace-only items skipped before yield) with correct page numbers and per-page reading order indices

#### Scenario: Tables become single text blocks
- **WHEN** a Docling item has `Type == "table"` and a non-empty `Text` containing a flattened cell sequence
- **THEN** the extractor yields one `PdfBlock` for the entire table, suitable for embedding as a single chunk
