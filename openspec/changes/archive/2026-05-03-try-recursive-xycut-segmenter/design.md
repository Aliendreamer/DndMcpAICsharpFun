## Context

PdfPig ships two page segmenters in `UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter`:

- **`DocstrumBoundingBoxes`** — implements the Docstrum algorithm (O'Gorman, 1993). Builds a graph of nearest-neighbour relationships between words and clusters by within-line / between-line / between-column distance distributions. Strong on free-form layouts; can over-cluster on regular column grids if column gaps are not significantly wider than line gaps.
- **`RecursiveXYCut`** — implements the RXY-Cut algorithm (Nagy, Seth, Viswanathan, 1992). Recursively cuts the page along the largest horizontal/vertical whitespace gap, splitting into smaller rectangles until cuts are no longer beneficial. Strong on regular column grids; can fail on free-form layouts with overlapping bounding boxes.

D&D rulebooks (PHB, DMG, MM) have a regular two-column body layout with stylised sidebars and stat blocks. RXY-Cut's geometric approach is theoretically a better fit for the body, but may fragment sidebars and stat blocks differently than Docstrum.

Reading-order detection (`UnsupervisedReadingOrderDetector`) operates on the resulting `TextBlock` list and is independent of which segmenter produced them — so swapping is safe at that layer.

## Goals / Non-Goals

**Goals:**
- Add the smallest possible code change that lets us A/B the two segmenters against the same PHB corpus and the same probe queries.
- Make the choice operator-controlled (config), not user-controlled (no per-request override needed).
- Preserve current behaviour when the new config is unset.
- Keep both implementations live so we can revert by flipping a string.

**Non-Goals:**
- Picking the winner in this change. The decision is data-driven and happens after the experiment.
- Tuning either segmenter's parameters (Docstrum has within-line / between-line / between-column multipliers; RXY-Cut has minimum-cell-area thresholds). Defaults only.
- Adding any other segmenter (custom column-aware, OCR-based, third-party). Those are bigger changes deferred to follow-ups.

## Decisions

**1. Configuration string, not enum.**
`Ingestion:BlockSegmenter` is `string` (`"docstrum"` | `"xycut"`). Alternatives considered: a typed enum. Rejected because future segmenters (`"docling"`, `"markerpdf"`, etc.) are likely sidecar services with different runtime concerns, and a string keeps the door open without an enum migration.

**2. Resolve at construction time, not per-call.**
`PdfPigBlockExtractor` reads the configured segmenter once in its constructor and stores the resolved `IPageSegmenter` instance. The per-page `ExtractFromPage` loop then calls the selected instance directly with no branching. Alternatives considered: resolve on every page. Rejected as wasteful — the value can't change at runtime.

**3. Invalid value falls back, doesn't throw.**
A typo in the config key (e.g. `"xycuts"` or `"DOCSTRUMM"`) logs a warning and uses Docstrum. Rejected throwing because we want the app to keep working with default behaviour rather than crash on a config typo.

**4. No new spec capability.**
The change is a configuration knob inside the existing `block-extraction` capability, not a new behaviour. We add scenarios to the existing capability rather than carving out a new one.

**5. Defaults preserve current behaviour.**
The default value `"docstrum"` means deployments with no config change see no difference. Operators opt into the experiment explicitly.

**6. We do not add a "compare against ground truth" test.**
A unit test that asserts which segmenter is selected is appropriate. A unit test that asserts RXY-Cut produces "better" output is not — quality is a human judgement against probe queries. The verification step in `tasks.md` includes the manual probe-query comparison.

## Risks / Trade-offs

- **[Risk]** RXY-Cut produces no improvement, or actively makes some pages worse (e.g. stat blocks fragmenting differently). → **Mitigation:** the experiment is explicitly opt-in via config. Default stays `"docstrum"`. We can revert by deleting the config key without touching code.
- **[Trade-off]** Two segmenters means two code paths to test. → Acceptable for a transient experiment. If RXY-Cut wins decisively, a follow-up change deletes the Docstrum branch.
- **[Risk]** RXY-Cut is sensitive to small gaps and may produce micro-blocks on pages with tight column spacing. → The existing `MinBlockChars=40` filter in `BlockIngestionOrchestrator` already drops fragments. Same safety net applies.
- **[Risk]** `RecursiveXYCut.Instance` may not exist in our PdfPig version. → Verify in the first task before writing code; if absent, abandon the change and jump straight to Docling.
