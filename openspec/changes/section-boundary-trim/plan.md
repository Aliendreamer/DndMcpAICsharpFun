# Section Boundary Trim Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Trim over-long programmatic sections at their true boundary (sibling heading, shaded sidebar, map label), as a one-off override pass over flagged entities and inside extraction for new books.

**Architecture:** One pure module `bookextractor/boundary.py` holds the rules over a small per-line feature record. A second module `bookextractor/boundary_align.py` maps OCR lines onto an entity's stored `entries` and removes cut lines from them ("stored text minus cut lines"). `bookextractor/boundary_fix.py` is the dev tool (sibling of `fivetools_fix.py`) that writes overrides + approvals. Extraction calls the same rules on a section's own lines.

**Tech Stack:** Python 3, pymupdf (page render), numpy, pytest; `uv` venv in `Tools/BookExtractor`.

**Spec:** `openspec/changes/section-boundary-trim/` — `design.md` (incl. "Settled details") and `specs/section-boundary-trim/spec.md`. Read both.

## Global Constraints

- No LLM, no GPU, no new dependency.
- Never re-extract a book; never touch an approved entity or an entity already carrying an override on `fields.entries` / `canonicalText`.
- Skip set: `system-reference-document`, `dragonborn-slice`. BGG is in scope. Ignore `.bak` files.
- Heading cut: candidate height ≥ 0.9 × anchor heading height AND ≥ 1.15 × median body line height; same column or later page; anchor on the entity's first provenance page; zero or >1 anchor matches → no cut; known sub-headings are never cut points.
- Anchor match: casefolded letters+digits only, id suffix `-pNNN` / `-N` ignored, similarity ≥ 0.85.
- Shade: ~15 on 0–255 vs per-page paper colour; read-aloud cue = previous line ends in `read:` / `reads:`.
- Map labels: `^MAP\s*\d`, `1 square = <n> feet` (any spacing/case), `^Side View$`.
- Overrides: write both `fields.entries` and `canonicalText` (same entry shape as existing: `entityId`, `field`, `value`, `extracted`). `canonicalText` = its existing first line (the heading) + `\n` + `\n`.join(new entries).
- Approve only when: tokens kept ≥ 50%; cut starts at a rule-identified line; kept text non-empty and starts with the entity's own heading/name; no failing check other than `text-complete` / `neighbour-absorbed`. Approval: `reason: "boundary trim"`, `waivedChecks` = the failing ones of those two, `at` = UTC ISO timestamp (same shape as `fivetools_fix`).
- Types in scope: Lore, Rule, God, Plane, Faction, Location.
- Extraction-time rules never approve.
- Regression gate: no MATCH fixture entry below token-F1 0.95 vs its own text; no auto-approvable FIXED entry worse vs corrected text. If unmet, stop and report numbers — do not loosen thresholds.
- Code reading/editing via Serena tools (project rule).

## Review Focus

1. A section whose heading is printed in odd OCR casing ("RisinG From ThE LAsT War") must still anchor — test in Task 2.
2. The same heading text twice on the first page (e.g. a sidebar repeating the section name) must produce no cut — test in Task 2.
3. A cut line whose text appears twice in the stored entries (ambiguous removal) must skip the entity, not remove both — test in Task 3.
4. A table-only Lore entity (rows like `3 | House Dimir`) must not have its rows read as sibling headings — test in Task 2 (`headlike` rejects `|` rows and digit-led lines).
5. An entity whose provenance page is missing from the OCR cache is skipped and counted, never crashes the pass — test in Task 5.

---

### Task 0 (controller, not a subagent): OCR cache copy

- [ ] Copy the `book_extractor_cache` Docker volume (find the exact name with `docker volume ls | grep book_extractor_cache`) to `$SCRATCH/ocr_cache/` (e.g. `docker run --rm -v <vol>:/c:ro -v $SCRATCH/ocr_cache:/o alpine cp -r /c/. /o/`). Needs `dangerouslyDisableSandbox` (docker.sock). Layout: `<sha>/ocr/NNN.json`; sha = PDF sha as `BookReader` computes it.
- [ ] Record the path; Tasks 1 and 5 take it as `--ocr-cache`.

### Task 1: Regression fixture + scorer

**Files:**
- Create: `Tools/BookExtractor/tests/boundary_fixture_build.py` (dev script, not a test)
- Create: `Tools/BookExtractor/tests/data/boundary_regression.json`
- Create: `Tools/BookExtractor/bookextractor/boundary_score.py`
- Test: `Tools/BookExtractor/tests/test_boundary_score.py`

**Interfaces:**
- Produces: `boundary_score.tokens(s: str) -> list[str]` (`re.findall(r"\w+", s.lower())`); `boundary_score.f1(a: str, b: str) -> float` (token-multiset F1, 0.0 when either empty).
- Produces fixture rows: `{id, book, verdict: "FIXED"|"MATCH", name, heading_line, entries: [str], corrected: str|null, checks: [{name, passed}], lines: [{text, page, col, h, box, shade}]}` — `lines` = reading-order lines (`layout.reading_order`) of the entity's provenance pages; `shade` precomputed with Task 2's `shade.line_shades` (build this fixture after Task 2's shade function exists, or recompute in Task 2 — implementer's choice; the committed fixture must contain shades).

- [ ] Step 1: tests `test_f1_identical == 1.0`, `test_f1_disjoint == 0.0`, `test_f1_empty == 0.0`, `test_f1_subset` (`f1("a b c d","a b") ≈ 0.667`).
- [ ] Step 2: run `cd Tools/BookExtractor && .venv/bin/pytest tests/test_boundary_score.py -q` → FAIL.
- [ ] Step 3: implement `boundary_score.py`.
- [ ] Step 4: build script reads ChatGPT answers from `/tmp/claude-1000/-home-aliendreamer-projects-DndMcpAICsharpFun/6ab16f2f-2ad0-495d-ad20-e45f0bab2658/scratchpad/gpt_full/ans/*.json` (`answer` starts `VERDICT: MATCH|FIXED|UNSURE`; corrected text follows `CORRECTED:`), skips UNSURE, takes entity data from `books/canonical/<book>.json` **at commit `4a348b6e`'s parent state for entries that have since been overridden** — simplest: use the `extracted` value of the entity's `fields.entries` override when one exists, else current `fields.entries`. OCR lines from `--ocr-cache`; PDFs from `books/`.
- [ ] Step 5: tests pass; fixture committed (keep it compact: no frags).
- [ ] Step 6: commit `test(bookextractor): boundary regression fixture and token-F1 scorer`.

### Task 2: Boundary rules + shade

**Files:**
- Create: `Tools/BookExtractor/bookextractor/boundary.py`
- Create: `Tools/BookExtractor/bookextractor/shade.py`
- Test: `Tools/BookExtractor/tests/test_boundary.py`

**Interfaces:**
- Produces: `@dataclass BLine(text: str, page: int, col: int, h: float, box: tuple[float,float,float,float], shade: float = 0.0)`.
- Produces: `@dataclass Cut(drop: set[int], start: int | None, rule: str | None)` — `start` = first dropped index of the heading/shaded rule that fired first in reading order; `rule` ∈ `{"heading", "shaded", "map"}`.
- Produces: `boundary.headlike(text: str) -> bool` (1–7 words, starts upper, no trailing `.,;:`, ≤1 lower-case word >3 letters, no `|`, not digit-led).
- Produces: `boundary.find_anchor(lines: list[BLine], name: str, first_page: int) -> int | None`.
- Produces: `boundary.cut(lines: list[BLine], name: str, first_page: int, sub_heading_idx: set[int] = frozenset(), shade_min: float = 15.0) -> Cut` — applies heading cut, shaded-sidebar and map-label rules; body median height over lines with ≥30 chars.
- Produces: `shade.line_shades(pdf: pathlib.Path, page: int, boxes: list[Box], ocr_dpi: int = 200, dpi: int = 100) -> list[float]` — per-line max-channel |background − paper|, background = mean of the brightest 30% of pixels in a band around the box (pad 3px y, 6px x at OCR scale), paper = per-page median of all lines' backgrounds. Algorithm reference: spike `proto.py:bg`.

- [ ] Step 1: failing tests with hand-built `BLine` lists:
  - `test_cut_at_sibling_heading` ("Sorcerer" h=30, body h=20, later "Warlock" h=30 same col → drop from Warlock, `rule=="heading"`).
  - `test_smaller_subheading_kept` (later heading h=24 < 0.9×30 → no drop).
  - `test_no_anchor_on_first_page_no_cut` (anchor only on page+1 → `drop == set()`).
  - `test_two_anchors_no_cut`.
  - `test_odd_casing_anchors` ("RisinG From ThE LAsT War" vs name "Rising from the Last War").
  - `test_sub_heading_idx_never_cut`.
  - `test_shaded_block_with_heading_dropped` / `test_read_aloud_box_kept` (prev line "…the following is read:").
  - `test_map_labels_dropped` (`MAP 3`, `1 square = 5 feet`, `Side View`).
  - `test_table_rows_not_headings` (`3 | House Dimir`, `10 Simic Combine` → `headlike` False).
- [ ] Step 2: run `.venv/bin/pytest tests/test_boundary.py -q` → FAIL.
- [ ] Step 3: implement `boundary.py`, `shade.py` (shade has one smoke test rendering page 1 of any PDF in `books/` if present, `pytest.skip` otherwise).
- [ ] Step 4: tests pass; full suite `.venv/bin/pytest -q` green.
- [ ] Step 5: commit `feat(bookextractor): section boundary rules and page shade`.

### Task 3: Align and remove from stored entries

**Files:**
- Create: `Tools/BookExtractor/bookextractor/boundary_align.py`
- Test: `Tools/BookExtractor/tests/test_boundary_align.py`

**Interfaces:**
- Consumes: `BLine`, `Cut`, `boundary_score.tokens`.
- Produces: `boundary_align.section_lines(lines: list[BLine], entries: list[str]) -> list[int]` — indices of page lines belonging to the stored text (a line belongs when ≥50% of its token trigrams occur in the entries' trigrams; <3 tokens: all tokens occur). Returned in reading order; `cut()` runs on `[lines[i] for i in result]`.
- Produces: `boundary_align.remove(entries: list[str], dropped: list[str]) -> list[str] | None` — removes each dropped line's text from the entries (whitespace-normalised substring match); returns `None` if any dropped text is not found exactly once; drops entries left empty; keeps paragraph boundaries otherwise.

- [ ] Step 1: failing tests: `test_remove_tail_paragraphs`, `test_remove_mid_paragraph_line` (line text inside a paragraph removed, rest kept), `test_remove_ambiguous_returns_none` (dropped text occurs twice), `test_remove_missing_returns_none`, `test_section_lines_ignores_other_column_text`.
- [ ] Step 2: run → FAIL. Step 3: implement. Step 4: pass.
- [ ] Step 5: commit `feat(bookextractor): align OCR lines to stored entries and remove cut lines`.

### Task 4: Regression gate

**Files:**
- Test: `Tools/BookExtractor/tests/test_boundary_regression.py`

**Interfaces:** Consumes Tasks 1–3; also `boundary_fix.approvable` from Task 5 — so define the gate predicate here first as `boundary.approvable(before: list[str], after: list[str], cut: Cut, name: str, checks: list[dict]) -> bool` in `boundary.py` (Task 5 reuses it).

- [ ] Step 1: test loads `tests/data/boundary_regression.json`, per row: `section_lines` → `cut` → `remove`; `after = remove(...) or entries`.
  - `test_match_entries_keep_f1`: every MATCH row `f1(join(after), join(entries)) >= 0.95`.
  - `test_approvable_fixed_never_worse`: every FIXED row where `approvable(...)` → `f1(join(after), corrected) >= f1(join(entries), corrected) - 0.005`.
  - `test_report_numbers` (always passes; prints improved/unchanged/worse for FIXED, approvable count, MATCH min F1 — controller pastes these to the owner).
- [ ] Step 2: implement `approvable` (gate per Global Constraints; unit tests for each clause in `test_boundary.py`).
- [ ] Step 3: run; if a gate fails, STOP and report the failing ids and numbers (no threshold change).
- [ ] Step 4: commit `test(bookextractor): boundary regression gate`.

### Task 5: `boundary_fix` dev tool

**Files:**
- Create: `Tools/BookExtractor/bookextractor/boundary_fix.py`
- Test: `Tools/BookExtractor/tests/test_boundary_fix.py`

**Interfaces:**
- Consumes: everything above; `BookReader`'s cache layout (`<cache>/<sha>/ocr/NNN.json`, `model.load_ocr_page`), `layout.reading_order`; overrides file shape `{overrides: [...], approvals: [...]}` as `fivetools_fix` writes it (reuse its load/save helpers if they exist, else mirror them).
- Produces CLI: `python -m bookextractor.boundary_fix --canonical books/canonical --books-dir books --ocr-cache <dir> [--books ggr,erlw] [--dry-run] [--report out.json]`.
- Produces: `run(canonical: Path, books_dir: Path, ocr_cache: Path, books: list[str] | None, dry_run: bool) -> dict` — report `{book: {candidates, trimmed, approved, flagged, skipped: {reason: n}}}`; skip reasons include `no-anchor`, `ambiguous-removal`, `missing-ocr-page`, `no-change`, `already-overridden`.
- PDF for a book: the book's `sourcePdf`/hash as the canonical file or `books/` registry records it (find it the way the spike's `todo.json` `pdf` field was derived — `gpt_full/books.json` maps book → pdf; read that mapping logic and reproduce from repo data, not from the scratch file).

- [ ] Step 1: failing tests on a tiny fake canonical + overrides + OCR-cache dir under `tmp_path` (shade monkeypatched to 0): `test_trim_writes_both_fields`, `test_approves_small_trim`, `test_large_cut_not_approved`, `test_other_failing_check_blocks_approval`, `test_skips_approved_and_overridden`, `test_skip_books`, `test_missing_ocr_page_skipped_and_counted`, `test_dry_run_writes_nothing`.
- [ ] Step 2: run → FAIL. Step 3: implement. Step 4: pass; full suite green.
- [ ] Step 5: commit `feat(bookextractor): boundary_fix trims flagged sections into overrides`.

### Task 6: Extraction-time wiring

**Files:**
- Modify: `Tools/BookExtractor/bookextractor/parsers/world.py` (where an entry's `own` lines are final, before `_text_check`), `parsers/rule.py` (same point, before its `_text_check`).
- Test: `Tools/BookExtractor/tests/test_world.py`, `tests/test_rule.py`

**Interfaces:** Consumes `boundary.cut`. Build `BLine`s from the section's `own` lines (`col` via `sections.page_column`); pass the section's known sub-heading line indices as `sub_heading_idx`; shade = 0 unless the parse context already has the page image available (no new rendering inside extraction unless `ctx` exposes the PDF — implementer checks `ParseContext`; if not available, rules heading + map only). Never approves.

- [ ] Step 1: failing tests: a world section fixture that swallows a same-size sibling heading now ends before it; a section with a smaller sub-heading is unchanged.
- [ ] Step 2: run → FAIL. Step 3: implement. Step 4: full suite green (existing world/rule tests must not change expectations; if one does, report it rather than editing it).
- [ ] Step 5: commit `feat(bookextractor): extraction ends world and rule sections at sibling headings`.

### Task 7 (controller): Run

- [ ] Dry run over all books → paste per-book report + Task 4 numbers to the owner; wait for go.
- [ ] Real run; `POST /admin/books/{id}/apply-overrides` per touched book (ids: MM1 PHB2 DMG3 ERLW4 XGE5 SCAG6 MTF7 MPMM8 TCE12 VRGR13 FTD14 EGW16 SCC17 GGR18; BGG and EEPC: look up via `GET /admin/books`); chown overrides to 1654 as before.
- [ ] Spot-check ~10 approvals against page images.
- [ ] Commit overrides + book files (`data(canonical): flagged sections trimmed at their printed boundary (one-off)`); tick `tasks.md`; update Serena `programmatic_extraction_status`.
