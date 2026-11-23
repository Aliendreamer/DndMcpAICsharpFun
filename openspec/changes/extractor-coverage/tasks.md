## 1. Measure

- [ ] 1.1 `coverage` in `ExtractionResult` (content lines, unheld, unheld lines with page) per the spec's definition; unit test on a small synthetic book (held, unheld, left-out, digit-repaired, hyphenated)
- [ ] 1.2 `fullbook_compare.py` prints each book's coverage (old vs new); `tools/coverage_scan.py <ocr-cache> [slugs]` scans committed canonical files (Declined excluded) with the same rule; test of the shared rule
- [ ] 1.3 Baseline: run the scan over all 16 books with HEAD, record per-book numbers in `baseline.md` in this change dir

## 2. Front and back matter

- [ ] 2.1 Credits / TOC / index page detection → `left_out`, no entities; fixture tests (PHB index page, MM credits page, a TOC page, a prose page with a cross-reference)

## 3. Keep unclaimed prose

- [ ] 3.1 Unclaimed runs in `_keep_unheld`: continuation append when certain, else flagged Lore named after the nearest preceding heading; fixture tests from MM p154 (Annam), EGW p34 (Kryn), PHB p159 (lifestyles), ERLW p18 (Kalashtar)
- [ ] 3.2 Drop-cap first letter restored; fixture test from TCE p8

## 4. Re-extract and gate

- [ ] 4.1 Whole-book compare on all 16 books with `--prev` (0 newly orphaned) and coverage (no book rises, total falls); fix regressions
- [ ] 4.2 Re-extract all 16 books live, apply overrides, run the post-override scan, commit data + `baseline.md` results
