# Extractor Boundary Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop the programmatic extractor's six boundary failures, measured against a frozen page-verified regression set, and make re-extraction retire or keep overrides instead of sending reviewed entities back to review.

**Architecture:** Python (`Tools/BookExtractor`) gains a page-serialisation entry point, a text normaliser, an append-only regression fixture + gate, and one rule per pattern. C# (`Features/Ingestion/Programmatic`) gains the same normaliser + token-F1, a three-way outcome in `OverrideMerger.Apply`, orphan migration, and a runner that writes the reduced overrides file under the book lock.

**Tech Stack:** Python 3.12 + pytest (venv `Tools/BookExtractor/.venv`), PyMuPDF; .NET 10, xunit + FluentAssertions.

**Spec:** `openspec/changes/extractor-boundary-fixes/` — `proposal.md`, `design.md` (decisions D1–D7), `specs/**/spec.md`.

## Global Constraints

- Python tests: `cd Tools/BookExtractor && .venv/bin/python -m pytest -q <path>`; C# tests: `dotnet test DndMcpAICsharpFun.Tests --filter <name>` (run with the sandbox disabled — git-crypt Config).
- Warnings are errors in every C# project; nullable on.
- Match = equal after normalising whitespace, line breaks and end-of-line hyphenation (`-` + newline joins the word). Case and punctuation are NOT normalised.
- Gate: fail on a lost match or a token-F1 drop > **0.005**. Migration: similarity ≥ **0.8**, next best ≥ **0.1** lower.
- The regression fixture is append-only; existing entries' expected values are never rewritten.
- The extractor service and app MUST NOT read 5etools data. No LLM anywhere.
- Serena for C# code reads/edits; commit each reviewed task straight to `main`; end commits with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Touching an endpoint's contract → update `DndMcpAICsharpFun.http` and `dnd-mcp-api.insomnia.json` in the same commit.

## Review Focus

1. Admin saves on `/review` while an extraction run is in flight → the run must not overwrite them (reload overrides inside the lock; Task 8 test).
2. An override whose field the new extraction no longer has (`extractedAbsent`, value now null) → treated as "not a match, not closer" → drift, never retired (Task 6 test).
3. Text-layer pages (no OCR cache file) → the fixture builder reads them from the PDF, so books without OCR still get entries (Task 3 test).
4. Structured fields (lists of `{name, entries}`) → compared via normalised JSON text, so a re-ordered-but-equal object still matches (Task 5 test).
5. Two overrides on one entity where one retires and one drifts → the retired one leaves the file, the other stays with its drift (Task 6 test).

---

### Task 1: Page serialisation + subset extraction entry point

**Files:**
- Create: `Tools/BookExtractor/bookextractor/pages_io.py`
- Test: `Tools/BookExtractor/tests/test_pages_io.py`

**Interfaces:**
- Produces: `page_to_dict(p: Page) -> dict`, `page_from_dict(d: dict) -> Page` (keys `number,width,height,source,lines[{text,box,height,frags[{text,score,box}]}]`), `extract_pages(pages: list[Page], book_slug: str) -> list[dict]` = `extract_book(pages, book_slug, set(ALL_TYPES)).entities` (all types: types claim lines from each other).

- [ ] **Step 1: Write failing tests** — `test_roundtrip_keeps_every_line` (load `tests/fixtures/ocr/mm/167.json` with `load_ocr_page`, roundtrip through dict, assert equal dataclasses); `test_extract_pages_matches_extract_book` (same page: `extract_pages([p], "mm14")` ids/texts equal `extract_book([p], "mm14", set(ALL_TYPES)).entities`, and include an entity named "Goblin").
- [ ] **Step 2: Run** `pytest tests/test_pages_io.py` → FAIL (module missing).
- [ ] **Step 3: Implement** the three functions in `pages_io.py`.
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(extractor): page serialisation + subset extraction entry point`.

### Task 2: Match normaliser (Python)

**Files:**
- Create: `Tools/BookExtractor/bookextractor/textnorm.py`
- Test: `Tools/BookExtractor/tests/test_textnorm.py`

**Interfaces:**
- Produces: `normalize_text(s: str) -> str` (`re.sub(r"-[ \t]*\n\s*", "", s)` then collapse all whitespace runs to one space, strip); `values_match(a, b) -> bool` (strings via `normalize_text`; lists/dicts recursively, dict key order ignored; other JSON scalars `==`).

- [ ] **Step 1: Write failing tests** — `normalize_text("ob-\nject") == "object"`; `normalize_text("a  b\n c") == "a b c"`; `not values_match("Fire", "fire")`; `values_match([{"name": "Bite", "entries": ["a\nb"]}], [{"entries": ["a b"], "name": "Bite"}])`; `not values_match(14, "14")`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(extractor): match normaliser`.

### Task 3: Append-only regression fixture builder (+ build the fixture)

**Files:**
- Create: `Tools/BookExtractor/tests/extraction_regression_build.py` (dev script, not collected by pytest — no `test_` prefix)
- Create: `Tools/BookExtractor/tests/data/extraction_regression.json.gz` (gzip; pages stored once per book)
- Test: `Tools/BookExtractor/tests/test_extraction_regression_build.py`

**Interfaces:**
- Consumes: `page_to_dict` (Task 1); `load_ocr_page`; `reader._layer_page`; `reader.pdf_sha256`.
- Produces: `build(books_dir: Path, ocr_cache: Path, existing: list[dict]) -> tuple[list[dict], list[str]]` (entries, skipped ids). File: `{"pages": {"<slug>:<n>": page dict}, "entries": [entry]}`, gzip. Entry: `{id, slug, type, name, pages: [int], ctx_pages: [int], expected: {"canonicalText": str, "fields": dict}, overridden: [field paths]}` — `ctx_pages` are page numbers resolved through the top-level `pages` map. Only entities with at least one override/approval whose reason does NOT start with `verified against 5etools` (page-verified / hand reviews; ~436). Pages = entity pages ∪ provenance pages, `ctx_pages` = those ±1. CLI: `python tests/extraction_regression_build.py --books-dir ../../books --ocr-cache <dir> [--out F]`.

- [ ] **Step 1: Write failing tests** with a tmp `books_dir` holding a 1-entity canonical file + overrides file (override on `canonicalText`, one approval-only entity) and a tmp OCR cache page: `test_override_value_is_expected`, `test_approved_entity_expects_extraction`, `test_existing_entry_is_never_rewritten` (pass `existing` with a different expected text → kept), `test_missing_page_is_skipped_and_named`, `test_layer_page_read_from_pdf` (no OCR file, page from a 1-page PDF made with `pymupdf` in tmp).
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement** `build` + CLI (entities = every id in `overrides[*].entityId ∪ approvals[*].entityId`; expected = canonical entity with each override's value set at its path).
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5 (controller, needs Docker):** `docker cp dndmcpaicsharpfun-book-extractor-1:/cache $SCRATCH/ocrcache`, run the builder against the repo `books/`, print entries / skipped counts and confirm at least one entry for each pattern's named case (Homunculus Servant, Gruul row page ggr p69, Adult Gold Dragon, an XGE encounter table, TCE Haunted, Werewolf). Missing case → note it; that pattern's task uses a hand-made test from the page's lines.
- [ ] **Step 6: Commit** builder, test and fixture: `test(extractor): page-verified regression fixture (N entries)`.

### Task 4: Regression run + baseline gate

**Files:**
- Create: `Tools/BookExtractor/tests/test_extraction_regression.py`
- Create: `Tools/BookExtractor/tests/regression_run.py` (shared scoring used by the test and the baseline update)
- Create: `Tools/BookExtractor/tests/data/extraction_regression.baseline.json`

**Interfaces:**
- Consumes: `page_from_dict`, `extract_pages` (Task 1), `values_match` (Task 2), `boundary_score.f1`; the gzip fixture format of Task 3 (`load_fixture() -> tuple[dict, list[dict]]` in `regression_run.py` resolves `ctx_pages` numbers to Page objects).
- Produces: `score_entry(entry: dict) -> dict` → `{id, found: bool, f1: float, match: bool}` (entity found by id; `match` = every `overridden` path `values_match`es expected, and `canonicalText` matches when overridden); `score_all() -> list[dict]`; `python tests/regression_run.py --update-baseline` rewrites the baseline; plain run prints `matches / improved / worse` vs baseline.

- [ ] **Step 1: Write the gate tests** — `test_no_lost_match` (baseline `match` true → now true), `test_no_f1_drop` (now ≥ baseline − 0.005).
- [ ] **Step 2: Implement** `regression_run.py`; generate the baseline with `--update-baseline`.
- [ ] **Step 3: Run** `pytest tests/test_extraction_regression.py` → PASS; run `python tests/regression_run.py` and paste the totals into the commit message.
- [ ] **Step 4: Commit** `test(extractor): regression gate + baseline (M/N matching)`.

### Task 5: C# normaliser + token-F1

**Files:**
- Create: `Features/Ingestion/Programmatic/TextMatch.cs`
- Test: `DndMcpAICsharpFun.Tests/Programmatic/TextMatchTests.cs`

**Interfaces:**
- Produces: `public static class TextMatch { string Normalize(string s); bool Matches(JsonNode? a, JsonNode? b); double TokenF1(string a, string b); string FlattenText(JsonNode? v); }` — same rules as Task 2; tokens = `\w+` lowercased, multiset F1 as `boundary_score.f1`; `FlattenText` joins every string leaf in document order with spaces (structured fields' text for F1).

- [ ] **Step 1: Failing tests** mirroring Task 2's five cases, plus `TokenF1("a b c","a b c")==1`, `TokenF1("a b","c d")==0`, `TokenF1("", "a")==0`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(overrides): text match normaliser + token F1`.

### Task 6: Three-way override outcome in `OverrideMerger.Apply`

**Files:**
- Modify: `Features/Ingestion/Programmatic/OverrideMerger.cs` (`MergeResult`, `Apply`, `ApplyOne`)
- Test: `DndMcpAICsharpFun.Tests/Programmatic/OverrideMergerTests.cs`

**Interfaces:**
- Consumes: `TextMatch` (Task 5).
- Produces: `MergeResult(IReadOnlyList<EntityEnvelope> Entities, IReadOnlyList<string> OrphanedEntityIds, IReadOnlyList<FieldOverride> Retired)` (Retired empty from `Reapply`).

- [ ] **Step 1: Failing tests** (use the existing `Grell`/`Ov` helpers): `Apply_retires_an_override_the_extraction_now_matches` (extracted `"3"`, override `"3"` baseline `"2"` → `Retired` has it, no drift); `Apply_keeps_an_override_quietly_when_extraction_got_closer` (canonicalText baseline "Grell text SIDEBAR junk", override "Grell text", new "Grell text junk" → no drift, value = override); `Apply_flags_drift_when_extraction_got_worse` (new "totally different" → drift); `Apply_unchanged_extraction_applies_without_drift`; `Apply_absent_field_is_never_retired` (new extraction lacks the field → drift); `Apply_retires_one_override_and_drifts_another` (two fields).
- [ ] **Step 2: Run** → FAIL.
- [ ] **Step 3: Implement** in `ApplyOne` (non-reapply): match → skip applying, record retired, no drift; equals baseline → apply; `F1(new,want) > F1(old,want)` (strings: `TokenF1`; JSON: `TokenF1(FlattenText(..))`) → apply, no drift; else apply + drift. `Reapply` path unchanged.
- [ ] **Step 4: Run** the whole `OverrideMergerTests` class → PASS (existing tests still green).
- [ ] **Step 5: Commit** `feat(overrides): retire on match, keep quietly when closer`.

### Task 7: Orphan migration

**Files:**
- Create: `Features/Ingestion/Programmatic/OverrideMigration.cs`
- Test: `DndMcpAICsharpFun.Tests/Programmatic/OverrideMigrationTests.cs`

**Interfaces:**
- Produces: `public sealed record MigrationResult(EntityOverrides Overrides, IReadOnlyList<(string From, string To)> Moved, IReadOnlyList<string> Orphaned);` `public static class OverrideMigration { MigrationResult Migrate(EntityOverrides o, IReadOnlyList<EntityEnvelope> previous, IReadOnlyList<EntityEnvelope> extracted); double NameSimilarity(string a, string b); }` — similarity = 1 − Levenshtein(slugA, slugB) / max(len); candidate = same `Type`, its `Page` or provenance pages contain the previous entity's `Page`; move iff best ≥ 0.8 and second < best − 0.1. Moves rewrite `EntityId` on both overrides and approvals.

- [ ] **Step 1: Failing tests** — `Caasting_time_moves_to_casting_time` (approval + override move, listed in `Moved`); `Two_equally_close_candidates_stay_orphaned`; `No_candidate_on_the_page_stays_orphaned`; `Different_type_is_never_a_candidate`; `Present_ids_are_untouched`.
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement.** **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(overrides): carry orphaned overrides to a certain rename`.

### Task 8: Runner writes the reduced overrides under the lock

**Files:**
- Modify: `Features/Ingestion/Programmatic/ProgrammaticExtractionRunner.cs` (`RunAsync`, `ProgrammaticRunResult`)
- Test: the runner's existing test class (find it with Serena `find_referencing_symbols` on `ProgrammaticExtractionRunner`); add one if none

**Interfaces:**
- Consumes: `OverrideMigration.Migrate` (Task 7), `MergeResult.Retired` (Task 6), `CanonicalJsonLoader.LoadAsync`, `OverridesStore.LoadAsync/SaveAsync`.
- Produces: `ProgrammaticRunResult(int Entities, int Accepted, int NeedsReview, IReadOnlyList<string> Orphans, JsonElement Report, int Retired, IReadOnlyList<string> Moved)` (`Moved` as `"from -> to"`).

- [ ] **Step 1: Failing tests** — `Run_retires_matching_override_in_the_file`; `Run_moves_renamed_override_in_the_file`; `Run_keeps_an_override_saved_during_the_run` (a store save between extraction and swap must survive: reload overrides **inside** `locks.AcquireAsync(slug)`, migrate + merge from that copy, then `SaveAsync` the reduced set before the swap).
- [ ] **Step 2: Run** → FAIL. **Step 3: Implement** (previous entities = current canonical file if it exists, else none). Extend `LogDone` with retired/moved counts.
- [ ] **Step 4: Run** → PASS. Check whether any endpoint returns `ProgrammaticRunResult` (grep its uses); if so, update `DndMcpAICsharpFun.http` + `dnd-mcp-api.insomnia.json`.
- [ ] **Step 5: Commit** `feat(extraction): run retires/migrates overrides under the book lock`.

### Tasks 9–14: Boundary patterns (one task each, same step shape)

Each task: **Files** = the module named in design D3 + `tests/test_<module>.py`; **Consumes** = `extract_pages`, the Task 3 fixture.

- [ ] **Step 1: Failing test** that loads the named fixture entries' `ctx_pages`, runs `extract_pages`, and asserts the behaviour below.
- [ ] **Step 2: Run** → FAIL for the stated reason.
- [ ] **Step 3: Implement** the smallest rule (design D3).
- [ ] **Step 4: Run** the new test, the module's tests, and `tests/test_extraction_regression.py` → PASS; run `python tests/regression_run.py --update-baseline` only if no entry got worse; record matches before → after.
- [ ] **Step 5: Commit** `fix(extractor): <pattern> (regression M→M')`.

| Task | Pattern | Module (D3) | Assertions |
| --- | --- | --- | --- |
| 9 | Stat-block end | `parsers/monster.py` | erlw Homunculus Servant `canonicalText` ends with "feet of you." and has `trait`, `action`, `reaction`; erlw yields entities named "Replicate Magic Item" and "Group Patrons" (keep-all-content); mpmm Sacred Statue `languages == ["the languages the eidolon knew in life"]` |
| 10 | Table row vs heading | `parsers/sections.py`, `names.py` | no ggr entity whose name starts "5 I Helped"; ggr p69–71 yield entities named "How Do I Fit In?", "An Izzet Party", "Rank and Renown" (Lore/Rule) |
| 11 | Two-column stat blocks | `layout.py` / `parsers/monster.py` | mm14 Adult Gold Dragon text contains "Weakening Breath" and `fields.legendary` exists; Jackalwere has `action` with "Sleep Gaze"; Adult Bronze Dragon text does not end with "," |
| 12 | Sibling tables | `parsers/sections.py` / `world.py` | xge p94–97 yield separate entities "Coastal Encounters (Levels 5-10)", "Coastal Encounters (Levels 11-16)", "Desert Encounters (Levels 1-4)"; tce Monsters' Desires yields "Beasts" and "Fiends" entities |
| 13 | Table continuations | `parsers/sections.py` / `rule.py` | no tce entity name contains "Oolp"; `tce.rule.haunted` text holds rows "01" and "00" |
| 14 | Sidebars / quotes | `boundary.py` | mm14 Werewolf Shapechanger entry lacks "Black" and the entity has `action`; phb14 Concentration text lacks "Schools of Magic" and a phb14 entity named "The Schools of Magic" exists (headed sidebar) |

### Task 15: Re-extract every programmatic book (controller + user)

- [ ] **Step 1:** Rebuild images: `docker compose up -d --build app book-extractor`.
- [ ] **Step 2:** For each book with a schema-"2" canonical file, one at a time: record the `/review` non-Lore flagged count, have the user run `POST /admin/books/{id}/extract-programmatic` (admin key), wait, then record retired / moved / orphaned (run log + result), the book's regression totals, and the flagged count after.
- [ ] **Step 3:** A book where a regression entry loses its match or drops > 0.005 F1 → `git checkout -- books/canonical/<slug>.*`, re-apply overrides, reopen the pattern task.
- [ ] **Step 4:** Commit per book `data(<slug>): re-extract with boundary fixes (retired R, moved M, orphaned O)`; record the per-book table in `tasks.md` 4.2.
