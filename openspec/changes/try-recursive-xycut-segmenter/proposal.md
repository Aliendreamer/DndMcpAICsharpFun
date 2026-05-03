## Why

Block ingestion currently uses PdfPig's `DocstrumBoundingBoxes` page segmenter to group words into blocks before reading-order detection. Empirically, on the PHB this produces text with severe multi-column scrambling — sentences from the left and right columns are interleaved word-by-word in the resulting block text. Sample retrieval result for "what is a bard":

> `"bards to to sidelines\nMany prefer stick the\nin\ntheir and\nusing magic inspire allies"`

…which should be: *"Many bards prefer to stick to the sidelines in combat, using their magic to inspire their allies."*

Embeddings still locate the right pages (the consumer LLM can recover meaning), but the text is degraded. Before committing to a heavier solution (Docling sidecar, MarkerPDF, custom column-aware extraction), we want a 5-minute experiment swapping Docstrum for PdfPig's `RecursiveXYCut` page segmenter, which uses a different algorithm (recursive horizontal/vertical cuts based on whitespace gaps) that may handle the PHB's regular two-column layout better.

The experiment is cheap (one line of code), additive (no behaviour change unless explicitly enabled via config), and produces hard data — re-ingest the PHB, run the same probe queries against the same corpus, eyeball the top-K text. If RXY-Cut wins, ship it. If it loses or ties, fall back to a heavier solution (next change: `docling-pdf-extraction` or `markerpdf-pdf-extraction`).

## What Changes

- Add `Ingestion:BlockSegmenter` configuration value (`"docstrum"` | `"xycut"`, default `"docstrum"` to preserve current behaviour) on `IngestionOptions`.
- `PdfPigBlockExtractor` reads the configured segmenter at construction and uses either `DocstrumBoundingBoxes.Instance.GetBlocks(words)` or `RecursiveXYCut.Instance.GetBlocks(words)` accordingly. Reading-order detection (`UnsupervisedReadingOrderDetector`) is unchanged.
- An invalid value SHALL log a warning and fall back to `"docstrum"`.
- Add tests covering: default selects Docstrum, `"xycut"` selects RXY-Cut, invalid value falls back to Docstrum.
- Update `Config/appsettings.json` to include the new key with the default value, so the knob is discoverable.
- Update `DndMcpAICsharpFun.http` with a one-line comment near the ingest-blocks example pointing at the config knob.
- No spec or behaviour changes for any other capability. The retrieval API, ingestion API, and Qdrant payload remain identical.

This is intentionally a *parallel-path* change, not a replacement. After comparison we either flip the default to `"xycut"` (or stay on `"docstrum"`) in a follow-up commit; the loser stays as a fallback knob for now in case some PDFs work better with one segmenter than the other.

## Capabilities

### New Capabilities
<!-- none -->

### Modified Capabilities

- `block-extraction`: gains a configuration-driven choice between two PdfPig page segmenters; behaviour with the default value is unchanged.
- `ingestion-pipeline`: documents the new `Ingestion:BlockSegmenter` knob alongside the existing knobs.

## Impact

- **Code**: ~30 lines net. Modifies `IngestionOptions`, `PdfPigBlockExtractor`, adds 3 small tests. No DI changes.
- **Config**: one new key, default preserves current behaviour.
- **API**: none.
- **Storage / migration**: none. Existing `dnd_blocks` points are not invalidated; re-ingestion overwrites by deterministic point IDs as today.
- **Operational**: to A/B, set `Ingestion:BlockSegmenter=xycut` (or via env var `Ingestion__BlockSegmenter=xycut`), restart the app, re-run `POST /ingest-blocks` for the test book, query against the new corpus.
- **Risk**: minimal. The two segmenters expose the same API surface (`IPageSegmenter.GetBlocks(words) → IReadOnlyList<TextBlock>`); we are just swapping which instance is called. Worst case: RXY-Cut produces blocks that are too coarse or too fine, the comparison shows no improvement, and the change is reverted by deleting the config key plus the branch.
