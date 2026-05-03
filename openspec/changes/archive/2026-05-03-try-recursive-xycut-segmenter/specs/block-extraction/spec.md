# block-extraction (delta)

## ADDED Requirements

### Requirement: The page segmenter is configurable
The system SHALL select the PdfPig page segmenter based on `Ingestion:BlockSegmenter` (default `"docstrum"`). Valid values are `"docstrum"` (uses `DocstrumBoundingBoxes.Instance`) and `"xycut"` (uses `RecursiveXYCut.Instance`). Reading-order detection (`UnsupervisedReadingOrderDetector.Instance`) SHALL be applied identically regardless of which segmenter produced the blocks.

#### Scenario: Default configuration uses Docstrum
- **WHEN** `Ingestion:BlockSegmenter` is unset or set to `"docstrum"`
- **THEN** `PdfPigBlockExtractor.ExtractBlocks` invokes `DocstrumBoundingBoxes.Instance.GetBlocks(words)` for each page

#### Scenario: xycut value selects RecursiveXYCut
- **WHEN** `Ingestion:BlockSegmenter` is set to `"xycut"` (case-insensitive)
- **THEN** `PdfPigBlockExtractor.ExtractBlocks` invokes `RecursiveXYCut.Instance.GetBlocks(words)` for each page

#### Scenario: Invalid value falls back to Docstrum with a warning
- **WHEN** `Ingestion:BlockSegmenter` is set to a value other than `"docstrum"` or `"xycut"`
- **THEN** the system logs a warning at startup naming the offending value and proceeds with Docstrum
