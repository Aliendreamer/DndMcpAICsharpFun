# marker-pdf-conversion Specification

## Purpose
TBD - created by archiving change replace-docling-with-marker. Update Purpose after archive.
## Requirements
### Requirement: Marker converts PDFs into page-aware structural items
`MarkerPdfConverter` SHALL implement `IPdfStructureConverter` by submitting the book's container path to the marker service (`POST /convert-by-path`), polling `GET /status/{jobId}` at `Marker:PollIntervalSeconds`, and fetching `GET /result/{jobId}` on completion. The Marker JSON block tree SHALL be mapped to `PdfStructureDocument`: `SectionHeader` blocks become heading items with level derived from the HTML heading tag and 1-based page numbers derived from Marker's 0-based page ids; other text-bearing leaf blocks become text items; `PageHeader`, `PageFooter`, `Picture`, and `Figure` blocks are skipped.

#### Scenario: Successful conversion maps headings and pages

- **WHEN** a PDF is converted and the marker service returns a JSON document with a `SectionHeader` block `<h2>FIREBALL</h2>` on page id `/page/4`
- **THEN** the returned document contains a heading item with text `FIREBALL`, level 2, page number 5

#### Scenario: Conversion failure surfaces the marker error

- **WHEN** the marker job reaches state `failed`
- **THEN** the converter throws with the marker-reported error message and no document is produced

#### Scenario: Conversion timeout is bounded

- **WHEN** the marker job does not complete within `Marker:ConversionTimeoutMinutes`
- **THEN** the converter throws a timeout error naming the marker service and the elapsed time

### Requirement: Dice-table captions are demoted from headings
During JSON mapping, a `SectionHeader` whose text matches a dice-caption pattern (`^d\d+\b`, case-insensitive) SHALL be emitted as a text item, not a heading item, so table captions cannot capture section context from real headings.

#### Scenario: d4 caption does not become a section

- **WHEN** Marker output contains `SectionHeader` blocks `BEASTS` followed by `d4 Desired Offering` followed by table text
- **THEN** the items contain a heading `BEASTS` and a text item `d4 Desired Offering`, and the table text groups under the `BEASTS` section

### Requirement: Conversion results are disk-cached with a converter discriminator
`PdfConversionDiskCache` SHALL cache converted documents at `<ConversionCacheDirectory>/<sha256>.marker.json`. Cache files without the converter discriminator (legacy `<sha256>.json` Docling files) SHALL NOT be served.

#### Scenario: Marker cache hit

- **WHEN** a PDF whose hash has a `<hash>.marker.json` cache file is converted
- **THEN** the cached document is returned without calling the marker service

#### Scenario: Legacy docling cache ignored

- **WHEN** a PDF whose hash has only a legacy `<hash>.json` cache file is converted
- **THEN** the marker service is called and the result is written to `<hash>.marker.json`

### Requirement: The marker service is a first-class compose dependency
The `marker` compose service SHALL build from `docker/marker/`, persist its model cache in a project-owned named volume, and expose a health check that reports healthy only after models are loaded. The `app` service SHALL depend on `marker` with `condition: service_healthy`.

#### Scenario: App waits for marker

- **WHEN** `docker compose up` starts the stack
- **THEN** `app` does not start until the marker health check passes

