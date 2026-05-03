# A/B Results — RecursiveXYCut vs Docstrum

**Date:** 2026-05-03
**Corpus:** Player's Handbook 2014, ingested as `dnd_blocks` with both segmenters in turn.

## Verdict

**Tie / no improvement.** RecursiveXYCut produces the same multi-column scrambling as Docstrum on PHB layouts. Default stays `"docstrum"`. Knob preserved as a fallback for future PDFs that may behave differently. Escalating to a real layout-aware tool (`docling-pdf-extraction`).

## Sample comparison — query `q=how do gods work in dnd`

### Docstrum top-1 (score 0.74)
> `"Arms eyes\nthe and prayer\nand upraised toward sun a\non lips, elf to with inner that\nhis an begins glow an light\nspills to his\nout heal battle-worn companions..."`

### xycut top-1 (score 0.80)
> `"make mental of gods are\nDM's\nappropriate. ]fyou're\nplaying deric a\nthe\nwhich your\nbackground, decide god deity\nAcolyte\nserves served, consider deity's\nthe suggested\nor and\ndomains when selecting character's domain."`

Both top hits are on-topic (cleric / deity content from the right pages). Both are equally unreadable due to multi-column word interleaving. xycut's section metadata was actually better — more results came back with `sectionTitle: "Appendix B - Gods of the Multiverse"` populated — but that's an artifact of bookmark-mapping, not segmenter quality.

## Why both fail

`PdfPig.Page.GetWords()` returns words in **PDF content-stream order** — already scrambled across columns before either segmenter runs. Docstrum and RXY-Cut both group words into blocks by bounding boxes but preserve input order within each block. So scrambled input → scrambled block text, regardless of segmenter.

The fix has to be applied at the *word* level (column-aware sorting before segmentation), or by replacing PdfPig with a layout-aware tool that detects columns natively (Docling, MarkerPDF, MinerU).

## Decision

- Default stays `"docstrum"`.
- `Ingestion:BlockSegmenter` knob is kept; cheap to leave in, useful if a future PDF works better with `xycut`.
- Next change: `docling-pdf-extraction` (Docling sidecar replaces PdfPig for the block-extraction step).
