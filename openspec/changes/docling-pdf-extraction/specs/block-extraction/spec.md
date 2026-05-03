# block-extraction (delta)

## MODIFIED Requirements

### Requirement: The page segmenter is configurable
The system SHALL select the block extractor based on `Ingestion:BlockSegmenter` (default `"docstrum"`). Valid values are `"docstrum"` (PdfPig + `DocstrumBoundingBoxes`), `"xycut"` (PdfPig + `RecursiveXYCut`), and `"docling"` (sidecar docling-serve via `IDoclingBlockExtractor`). For PdfPig-based values, reading-order detection (`UnsupervisedReadingOrderDetector.Instance`) SHALL be applied identically. For the `"docling"` value, reading order is supplied by docling-serve and `UnsupervisedReadingOrderDetector` is not invoked.

#### Scenario: Default configuration uses PdfPig + Docstrum
- **WHEN** `Ingestion:BlockSegmenter` is unset or set to `"docstrum"`
- **THEN** the resolved `IPdfBlockExtractor` is `PdfPigBlockExtractor` configured with `DocstrumBoundingBoxes.Instance`

#### Scenario: xycut value selects PdfPig + RecursiveXYCut
- **WHEN** `Ingestion:BlockSegmenter` is `"xycut"`
- **THEN** the resolved `IPdfBlockExtractor` is `PdfPigBlockExtractor` configured with `RecursiveXYCut.Instance`

#### Scenario: docling value selects DoclingBlockExtractor
- **WHEN** `Ingestion:BlockSegmenter` is `"docling"`
- **THEN** the resolved `IPdfBlockExtractor` is `DoclingBlockExtractor`, which delegates to `IDoclingPdfConverter`

#### Scenario: Invalid value falls back to Docstrum with a warning
- **WHEN** `Ingestion:BlockSegmenter` is set to a value other than the three above
- **THEN** the system logs a warning at startup naming the offending value and proceeds with `PdfPigBlockExtractor` + Docstrum
