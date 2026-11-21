# Tasks

## 1. Regression set (safety net)

- [ ] 1.1 Add the extractor entry point that assembles entities of one type from pre-OCR'd page lines (same reader → pages → parsers → assemble path), with tests
- [ ] 1.2 Add the shared match normaliser (whitespace, line breaks, end-of-line hyphenation) in Python, with tests
- [ ] 1.3 Write `tests/extraction_regression_build.py`: append-only merge into `tests/data/extraction_regression.json` from overrides + canonical files + an OCR-cache copy (entity pages ±1, expected text/fields, overridden fields; skipped entries named; existing entries never rewritten)
- [ ] 1.4 Controller: copy the OCR cache out of the book-extractor container, build the fixture from commit 2851c5f0's overrides, check every pattern has a real case, commit it
- [ ] 1.5 Write `tests/test_extraction_regression.py`: run each entry through the entry point, score token-F1 and field matches, print totals
- [ ] 1.6 Add the committed baseline + `--update-baseline`; gate fails on a lost match or F1 drop > 0.005; record today's baseline

## 2. Overrides: retire-on-match and orphan migration (C#)

- [ ] 2.1 Failing tests in the `OverrideMerger` tests for the four D4 cases: matches override (normalised) → retired; equals baseline → applied, no drift; closer to override → kept, no drift; otherwise → drift
- [ ] 2.2 Implement the three-way outcome in `OverrideMerger.Apply` (not `Reapply`) with a C# normaliser matching the Python one; return retired overrides and count them
- [ ] 2.3 Failing tests for orphan migration: certain single match moves (caasting-time → casting-time), two close candidates stay orphaned, no candidate stays orphaned
- [ ] 2.4 Implement migration in the extraction run using the previous canonical file (type + page + name similarity ≥ 0.8, margin 0.1)
- [ ] 2.5 Write the reduced/migrated overrides file under the book's canonical lock; add retired/moved/orphaned to the run result; update `.http` + `.insomnia.json` if an endpoint returns that result

## 3. Boundary patterns (Python, test first, one at a time)

- [ ] 3.1 Stat-block end: failing tests for Homunculus Servant and Sacred Statue from the fixture; implement in `parsers/monster.py`; update baseline
- [ ] 3.2 Table row vs heading: failing test for the GGR p69 Gruul row; implement; verify the Izzet sections become entities; update baseline
- [ ] 3.3 Two-column stat blocks: failing tests for Adult Gold Dragon and Jackalwere (and Adult Bronze Dragon); implement; update baseline
- [ ] 3.4 Sibling tables: failing tests for an XGE random-encounter run and TCE Monsters' Desires; implement; update baseline
- [ ] 3.5 Table continuations: failing tests for TCE Haunted and Unraveling Magic ("Oolp Effect"); implement; update baseline
- [ ] 3.6 Sidebars and quotes: failing tests for Werewolf and Concentration; implement in `boundary.py`; update baseline
- [ ] 3.7 Full extractor test suite green; regression totals recorded in this change

## 4. Re-extract and report

- [ ] 4.1 Rebuild the app and book-extractor images
- [ ] 4.2 Re-extract every programmatic book one at a time; per book record retired / moved / orphaned overrides, regression totals and review-list count before → after
- [ ] 4.3 Revert and revisit any book where a regression-set entity loses its match or drops > 0.005 token-F1
- [ ] 4.4 Commit re-extracted canonical + overrides files per book

## Workflow follow-up

- Archive the change with `openspec archive extractor-boundary-fixes -y`, then run the `skill-optimizer` skill
- Next change: the coverage scan (text in no entity / in two entities) over all books
