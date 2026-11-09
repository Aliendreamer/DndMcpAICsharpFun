## 1. Regression fixture

- [ ] 1.1 Build a compact fixture from the 258 page-checked answers (id, book, pages, our text, corrected text, verdict) into `Tools/BookExtractor/tests/data/`
- [ ] 1.2 Add a token-F1 scorer helper for tests and a baseline test recording current scores

## 2. Boundary rules in the extractor

- [ ] 2.1 Heading cut in `parsers/sections.py` reusing existing heading sizes; anchor on own heading on first page; skip known sub-headings (unit tests incl. continuation-page and sub-heading cases)
- [ ] 2.2 Pixel shade measurement from the rendered page (CPU, pymupdf) and shaded-block-with-own-heading exclusion; read-aloud boxes kept (unit tests)
- [ ] 2.3 Map-label exclusion (unit tests)
- [ ] 2.4 Wire the rules into world and rule section building; existing extractor tests stay green
- [ ] 2.5 Regression test over the fixture using production OCR cache: MATCH ≥ 0.95, no auto-approvable FIXED worse; report numbers to the owner

## 3. Trim pass dev tool

- [ ] 3.1 `bookextractor/boundary_fix.py` (mirrors `fivetools_fix`): load canonical + cached OCR, rebuild flagged sections, apply rules, write overrides in existing shape; skip SRD/dragonborn-slice, approved, already-overridden
- [ ] 3.2 Auto-approve gate (≥50% tokens kept, rule-identified cut, starts with own heading) with approval reason `boundary trim`
- [ ] 3.3 `--dry-run` and `--report` with per-book trimmed/approved/still-flagged counts (tests)

## 4. Run

- [ ] 4.1 Dry run over the 16 books; report counts to the owner before writing anything
- [ ] 4.2 Real run; apply-overrides per book; spot-check a sample of approvals against page images
- [ ] 4.3 Commit overrides + book files per book (BGG included); update Serena `programmatic_extraction_status`
