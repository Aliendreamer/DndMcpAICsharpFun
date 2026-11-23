# Extractor Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every printed content line of a book is held by an entity, a note, or a left-out-on-purpose rule, and a
coverage number per book proves it.

**Architecture:** A small `bookextractor/coverage.py` module defines content lines and the held test; `extract_book`
attaches a `coverage` dict to its result; `fullbook_compare.py` and a new `tools/coverage_scan.py` print it.
Front/back-matter pages put their lines in `left_out`; `_keep_unheld` gains unclaimed-run rescue; a drop-cap rule
restores lost first letters. The controller then re-extracts all 16 books.

**Tech Stack:** Python 3 (Tools/BookExtractor, pytest, `.venv`), OCR cache JSON pages, fixture file
`tests/data/fullbook_cases.json.gz`.

**Spec:** `openspec/changes/extractor-coverage/` (proposal.md, design.md, specs/extraction-coverage/spec.md)

## Global Constraints

- Content line: OCR line of 30+ characters, OCR score >= 0.9 (minimum over the line's fragments), reduced text
  (lower-case `[a-z0-9]` only) of 20+ characters.
- Held: the reduced line (trailing `-` dropped first), or the reduced `fix_digits(line)`, is a substring of the reduced
  joined text + notes of the book's entities; or the line text is contained in a `left_out` text of the same page
  (containment, as `fullbook_compare.py` does). The committed-file scan excludes `Declined` entities.
- No book's unheld count may rise; the corpus total must fall. `fullbook_compare --prev <base>`: 0 newly orphaned
  non-quote lines.
- Rescued runs: append to the previous entity only when its last line directly precedes the run in reading order AND
  (run starts lower-case OR that entity's text does not end with `.`, `!`, `?`, `:` or a closing quote); otherwise a
  flagged Lore (failed `text-complete`, reason contains "unclaimed prose") named after the nearest preceding heading
  line on the page or the previous page, else its first words.
- Never drop text to make a number fall. Python code only; no C# change. Run tests with
  `cd Tools/BookExtractor && .venv/bin/python -m pytest -q` (sandbox may need to be disabled; the full suite takes ~7 min).
- Commit to `main` after each reviewed task.

## Review Focus

- A two-column page where the run sits in the right column but the previous entity ended in the left column foot:
  must append (reading-order neighbour), not create Lore — pinned in Task 4 (Brass Dragon style case).
- A stat block followed by headingless prose: the prose must NOT be glued into the Monster — pinned in Task 4 (MM p154).
- An index-like rules page (a spell list "Fireball, 3rd") must not become left out — pinned in Task 3.
- A drop-cap letter that is really a list marker or a page letter ("D" tab) must not be prefixed — pinned in Task 5.
- Lines held only by notes count as held — pinned in Task 1.

---

### Task 1: Coverage measure + tools

**Files:**
- Create: `Tools/BookExtractor/bookextractor/coverage.py`
- Modify: `Tools/BookExtractor/bookextractor/assemble.py` (`ExtractionResult`, end of `extract_book`)
- Modify: `Tools/BookExtractor/tests/fullbook_compare.py` (print coverage old/new per book)
- Create: `Tools/BookExtractor/tools/coverage_scan.py`, `Tools/BookExtractor/tests/fullbook_cases_add.py`
- Test: `Tools/BookExtractor/tests/test_coverage.py`

**Interfaces:**
- Produces: `reduce_text(text: str) -> str`; `is_content_line(text: str, score: float) -> bool`;
  `@dataclass Coverage(content_lines: int, unheld: list[tuple[int, str]])` with `to_dict() -> {"contentLines": int,
  "unheld": int, "unheldLines": [{"page": int, "text": str}]}`;
  `measure(lines: Iterable[tuple[int, str, float]], held_texts: Iterable[str], left_out: Iterable[tuple[int, str]]) -> Coverage`.
- Produces: `ExtractionResult.coverage: dict` (the `to_dict()` of the extraction's own measure; entities' canonicalText +
  notes text as held texts).
- Produces: `fullbook_cases_add.py <ocr-cache> <case> <slug> <page...>` appends OCR pages (via `load_ocr_page` +
  `page_to_dict`) and a case to `tests/data/fullbook_cases.json.gz` (fileHash read like `fullbook_compare.py` does).
- Produces: `tools/coverage_scan.py <ocr-cache> [slug...]` (run from repo root): per book `slug contentLines unheld`,
  writes `<out>/<slug>.gaps.txt` (`--out`, default `.`) listing unheld lines by page.

- [ ] **Step 1: Write failing tests** in `test_coverage.py`:
  `test_unheld_line_listed_with_page` (a 40-char prose line in no text → `unheld == [(5, line)]`, content 1);
  `test_short_or_low_score_lines_are_not_content` (29 chars → not content; score 0.89 → not content);
  `test_hyphenated_and_digit_repaired_lines_are_held` ("…its abys-" held by "…its abyssal"; "takes ld6 fire dam…" held by "1d6");
  `test_left_out_line_is_held`; `test_line_held_only_by_notes_is_held`;
  `test_extract_book_result_has_coverage` (small synthetic page via existing helpers in `test_assemble.py` →
  `result.coverage["contentLines"] >= 1` and keys present).
- [ ] **Step 2:** Run `.venv/bin/python -m pytest -q tests/test_coverage.py` → FAIL (module missing).
- [ ] **Step 3:** Implement `coverage.py`, wire `ExtractionResult.coverage` (default empty dict) at the end of
  `extract_book` using page lines (score = min fragment score), entity canonicalText + notes, final `left_out`.
- [ ] **Step 4:** Add coverage print to `fullbook_compare.py` (`coverage: old N/M unheld, new N/M unheld`) and write
  `coverage_scan.py` (same `measure`, held texts = non-Declined entities of `books/canonical/<slug>.json`, pages from
  `<cache>/<fileHash>/ocr/*.json`) and `fullbook_cases_add.py`.
- [ ] **Step 5:** Run the new tests + `tests/test_assemble.py` → PASS. Commit
  `feat(extractor): coverage measure (unheld content lines) + scan tools`.

### Task 2: Baseline (controller)

- [ ] Run `coverage_scan.py` over all 16 books at HEAD (committed files) and `fullbook_compare.py --ref HEAD` coverage
  for the raw extractor; write both tables to `openspec/changes/extractor-coverage/baseline.md`; commit.

### Task 3: Front and back matter left out

**Files:**
- Modify: `Tools/BookExtractor/bookextractor/pages.py` (`classify_page`), `bookextractor/assemble.py` (`extract_book`:
  lines of `toc`/`index`/`credits` pages appended to `left_out`)
- Test: `Tools/BookExtractor/tests/test_pages.py`, fixtures via `fullbook_cases_add.py` (PHB 314–317, MM 3, ERLW 3, a
  TOC page, PHB 159 as the prose control, and a spell-list rules page such as PHB 207)

**Interfaces:** `classify_page(page) -> PageKind` unchanged signature; new detection only.

- [ ] **Step 1: Failing tests:** `test_see_style_index_page_is_index` (PHB 314–317 each `== "index"`);
  `test_credits_by_roles_is_credits` (pages with 3+ lines matching
  `^(Lead |Additional |Art |Managing |Senior )?(Designers?|Developers?|Producers?|Writers?|Editors?|Directors?|Illustrators?|Cartographers?|Contributors?|Proofreaders?|Playtesters?|Managers?)\b.*:` → `"credits"`);
  `test_prose_page_with_one_see_reference_is_rules` (PHB 159);
  `test_spell_list_page_is_rules`; `test_front_back_matter_lines_are_left_out` (extract the PHB index case →
  every content line of those pages is in `result.left_out`, no entity has `page` on them).
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement (index: ratio of lines with `\bSee\b` or `_INDEX_RANGE` over the
  page's lines of 2+ words, threshold tuned so all tests pass). **Step 4:** Run tests + `tests/test_pages.py` → PASS.
- [ ] **Step 5:** Commit `feat(extractor): credits/TOC/index pages left out on purpose`.

### Task 4: Keep unclaimed prose

**Files:**
- Modify: `Tools/BookExtractor/bookextractor/assemble.py` (`_keep_unheld` and the call site that passes claimed lines)
- Test: `Tools/BookExtractor/tests/test_keep_unclaimed.py`, fixtures via `fullbook_cases_add.py`: MM 153–155
  (Annam), EGW 33–34 (Kryn), PHB 158–159 (lifestyles), ERLW 18–19 (Kalashtar), MM 107 (Brass Dragon right column)

**Interfaces:** uses `measure`/`reduce_text` from Task 1 in tests.

- [ ] **Step 1: Failing tests:**
  `test_unclaimed_paragraph_after_stat_block_is_flagged_lore` (MM 154: "When the giants' ancient empires fell, Annam"
  is held; holder type `Lore`, has failed `text-complete` with "unclaimed prose"; no Monster holds it);
  `test_column_continuation_appends_to_section` (MM 107: "that happens, the dragon often responds in kind" is in
  `mm14.lore.brass-dragon`'s canonicalText);
  `test_kryn_kalashtar_lifestyle_text_is_held` (one distinctive line from each of EGW 34, ERLW 18, PHB 159 is held);
  `test_fixture_cases_unheld_does_not_rise` (for every existing case in `fullbook_cases.json.gz`, unheld count with the
  new code <= count recorded in the test as a dict literal measured before the change — measure first, paste numbers).
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement per Global Constraints (rescue runs of lines no entity claimed, in
  reading order of `ordered[page]`; heading line = an existing heading test used by the parsers, e.g. `heading_words`).
- [ ] **Step 4:** Run new tests + `tests/test_fullbook_*.py tests/test_assemble.py tests/test_margin_notes.py` → PASS.
- [ ] **Step 5:** Commit `feat(extractor): keep unclaimed prose (append continuation or flagged Lore)`.

### Task 5: Drop-cap first letter

**Files:** Modify `Tools/BookExtractor/bookextractor/assemble.py` or the opening helper `_drop_cap_opening` in
`sidebars.py`; Test `tests/test_drop_cap.py` with fixtures TCE 8, TCE 106 (via `fullbook_cases_add.py`).

- [ ] **Step 1: Failing tests:** `test_drop_cap_letter_restored` (TCE 8 entity text contains "When you make your D&D"
  case-insensitively and not "hen you make" at a word start); `test_magic_opening_restored` (TCE 106 contains
  "Magic is everywhere" case-insensitively); `test_tab_letter_not_prefixed` (an MM page with the side "D" tab, e.g.
  MM 107: no entity text starts with "D" + the first word of the page's first paragraph glued).
- [ ] **Step 2:** FAIL. **Step 3:** Implement: a single-letter line (or 1-char fragment) with height >= 2x the median
  body line height, left of or above the opening's first line, prefixed when the opening's first word + letter forms a
  known word (vocabulary) or the opening starts lower-case. **Step 4:** PASS + `tests/test_assemble.py`.
- [ ] **Step 5:** Commit `feat(extractor): restore drop-cap first letters`.

### Task 6: Whole-book gate and re-extraction (controller)

- [ ] Run `fullbook_compare.py --cache <ocrcache> --ref <base> --prev <base>` on all 16 books (base = commit before
  Task 3). Gate: 0 newly orphaned non-quote lines; coverage unheld per book not higher than base; total falls. Fix or
  rule on each regression.
- [ ] Re-extract all 16 books live (`runall.sh`), apply overrides, run `coverage_scan.py` (post-override), append
  results to `baseline.md`, commit data. New flagged entities go to `/review` (AI review loop if many).
