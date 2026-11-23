## Context

`extract_book` returns entities plus `left_out` (lines dropped on purpose: art text, captions, margin-note strays).
`_keep_unheld` rescues two kinds of unheld lines: those an entity claimed but pruned from its own text, and opening
runs. Everything else no entity claims is lost without a trace. The measurement script used to find this
(`gapscan.py`, scratchpad) normalises text to `[a-z0-9]` and tests containment.

## Goals / Non-Goals

**Goals:** a coverage number per book that a change must not raise; rescue unclaimed prose; stop counting front/back
matter; restore drop-cap first letters; re-extract and commit all books.

**Non-Goals:** splitting entities that hold several sections (XGE/EGW/BGG multi-section entities, `tce.lore.barbarian`);
new entity types; C#-side changes; LLM help.

## Decisions

- **Content line** = an OCR line of 30+ characters with OCR score >= 0.9 whose normalised form has 20+ characters.
  **Held** = its normalised form (trailing hyphen dropped; digit-repaired form also tried) is a substring of the
  normalised text + notes of the book's entities (Declined excluded in the post-override scan), or the line is in
  `left_out`. Same rule in the extractor's `coverage` and in `coverage_scan.py`, so numbers compare.
- **Front/back matter**: a page is credits when 3+ lines match a credits role (`Designer|Developer|Producer|Writer|
  Editor|Art Director|Illustrator|Cartograph|Proofread|Playtest|Manager|Lead|Additional Contributors`) followed by a
  colon; table of contents when 5+ lines end in dotted leaders or a page number after a title; index when 10+ lines
  match `<term>. See <term>` or `<term>, <number>` patterns. Their lines go to `left_out`; no entity starts there.
- **Unclaimed runs**: walk each page's lines in reading order; a maximal run of unheld lines that `qualifies()`
  (prose, not quote box, not epigraph) and that no entity claimed is a job. It continues the previous entity when that
  entity's last line is the line just before the run in reading order and the run starts lower-case or the previous
  text does not end a sentence; else it becomes a flagged Lore (`text-complete` advisory reason "unclaimed prose")
  named after the nearest preceding heading line on the page or the previous page, else its first words.
- **Drop cap**: a 1-letter line (or a letter OCR'd far left/above, height >= 2x body) immediately before an opening
  line starting lower-case or with a capitalised word missing its first letter is prefixed to that line.
- **Gate**: per book, `unheld` after the fixes is not higher than before for any book and the corpus total falls;
  `fullbook_compare --prev <base>` reports 0 newly orphaned non-quote lines.

## Risks / Trade-offs

- Attaching a run to the wrong entity glues foreign text → only when the reading-order neighbour test is certain;
  otherwise a separate flagged Lore (the review page decides).
- More flagged Lore on `/review` → acceptable; it is text that exists in the book. Bulk review uses the AI loop.
- Front/back-matter rules could swallow a real page → thresholds require many matching lines; fixture tests on real
  pages of PHB index, MM credits, TOC pages and a normal prose page.
