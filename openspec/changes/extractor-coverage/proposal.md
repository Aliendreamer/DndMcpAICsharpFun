## Why

A whole-corpus scan (2026-11-23) found about 12,400 OCR content lines (30+ characters, pages past the front matter) that
no entity of the committed canonical files holds. Review cannot see them: the entities they belong to look complete,
so nothing is flagged. Samples are real prose: EGW god sections (Kord, Gruumsh), the Annam passage of MM Giants,
ERLW Kalashtar and Sharn districts, PHB lifestyle expenses, TCE chapter openings, the TCE Summon Elemental spell.
The extractor's keep-all-content rule rescues only lines an entity claimed and then pruned, and openings; a run of
prose no entity claimed is dropped silently. Part of the corpus gap is also stale canonical files (PHB was not
re-extracted after the boundary fixes).

## What Changes

- **Coverage measure.** The extractor computes, per book, the content lines no entity holds and no left-out rule
  covers (`coverage` in its result: content-line count, unheld count, unheld lines with page). `fullbook_compare`
  prints it; a repo tool runs the same scan over the committed canonical files (overrides and declines applied).
- **Front and back matter left out on purpose.** Credits, table-of-contents and index pages are recognised and their
  lines recorded as left out, not counted as gaps and not turned into entities.
- **Unclaimed prose is kept.** Any run of unheld prose lines (not only pruned or opening runs) is kept: appended to
  the entity it continues when certain (same column flow, lower-case or mid-sentence start right after that entity's
  last line), else a flagged Lore named after the nearest preceding heading.
- **Drop-cap openings.** An opening whose first letter is a separately set drop cap keeps that letter ("Hen you make"
  becomes "When you make").
- **Re-extract all 16 books** and commit; the gate is the coverage count, next to the existing `--prev` keep-all gate.

## Capabilities

### New Capabilities

- `extraction-coverage`: measuring and enforcing that every printed content line of a book is held by an entity,
  a note, or a left-out-on-purpose rule.

### Modified Capabilities

None.

## Impact

- `Tools/BookExtractor/bookextractor/assemble.py` (keep-unheld, result), `sidebars.py` (left-out rules), tests and
  fixtures under `Tools/BookExtractor/tests/`; `tests/fullbook_compare.py`; a new `Tools/BookExtractor/tools/coverage_scan.py`.
- Data: all 16 `books/canonical/<slug>.json` re-extracted; new flagged Lore entities go to `/review`.
- No C# or endpoint change.
