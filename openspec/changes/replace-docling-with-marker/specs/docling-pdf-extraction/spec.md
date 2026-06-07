## REMOVED Requirements

### Requirement: IDoclingPdfConverter calls docling-serve to convert PDFs

**Reason**: Docling is fully replaced by Marker (`marker-pdf-conversion`); docling-serve and its client are deleted.

### Requirement: DoclingDocument carries page-aware structural items

**Reason**: Superseded by `PdfStructureDocument` under `marker-pdf-conversion` — same shape, converter-neutral naming.

### Requirement: DoclingBlockExtractor adapts Docling items to PdfBlock

**Reason**: The block extractor now adapts `PdfStructureItem`s produced by Marker; behavior carried over under `marker-pdf-conversion` and `ingestion-pipeline`.
