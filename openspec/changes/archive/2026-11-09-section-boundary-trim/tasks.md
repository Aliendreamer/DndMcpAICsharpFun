## 1. Regression fixture

- [x] 1.1 Build a compact fixture from the page-checked answers (id, book, pages, our text, corrected text, verdict) into `Tools/BookExtractor/tests/data/`
- [x] 1.2 Add a token-F1 scorer helper for tests and a baseline test recording current scores

## 2. Boundary rules in the extractor

- [x] 2.1 Heading cut (anchor on own heading on first page; skip known sub-headings; sibling-title evidence)
- [x] 2.2 Pixel shade measurement from the rendered page and shaded-block-with-own-heading exclusion; read-aloud boxes kept
- [x] 2.3 Map-label exclusion (label-like lines only)
- [x] 2.4 Wire the rules into world and rule section building with the same guards; existing extractor tests stay green
- [x] 2.5 Regression test over the fixture: MATCH ≥ 0.95, no approvable FIXED worse; numbers reported to the owner

## 3. Trim pass dev tool

- [x] 3.1 `bookextractor/boundary_fix.py`: load canonical + cached OCR, rebuild flagged sections, apply rules, compute overrides; skip SRD/dragonborn-slice, approved, already-overridden
- [x] 3.2 Approval gate (≥50% tokens kept, rule-identified cut, opening tokens unchanged, waivable checks only)
- [x] 3.3 `--dry-run` and `--report` with per-book counts; real runs refused

## 4. Run

- [x] 4.1 Dry run over the 16 books; counts reported to the owner
- [x] 4.2 Real run CANCELLED by the owner (2026-11-09): the final review found the removal step deletes words mid-sentence (9 of 12 would-be approvals damaged); see design.md "Outcome"
- [x] 4.3 Record the outcome in Serena `programmatic_extraction_status`
