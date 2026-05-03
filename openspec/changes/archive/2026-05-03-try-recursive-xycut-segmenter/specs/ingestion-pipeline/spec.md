# ingestion-pipeline (delta)

## ADDED Requirements

### Requirement: Ingestion options expose the page-segmenter knob
The `IngestionOptions` configuration class SHALL expose a `BlockSegmenter` property (string, default `"docstrum"`). The value is consumed by `PdfPigBlockExtractor` to choose between `DocstrumBoundingBoxes` and `RecursiveXYCut`. No other ingestion behaviour is affected by this knob.

#### Scenario: Default value preserves existing behaviour
- **WHEN** the application starts with no `Ingestion:BlockSegmenter` value in any configuration source
- **THEN** ingestion proceeds exactly as it did before this change, using `DocstrumBoundingBoxes`
