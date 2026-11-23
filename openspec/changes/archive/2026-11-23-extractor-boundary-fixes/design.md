# Design

## Context

- The programmatic extractor is the Python service `Tools/BookExtractor` (FastAPI, PyMuPDF + OCR, no LLM). Page
  reading and column order live in `layout.py`/`reader.py`; section ends in `boundary.py`; headings, tables and world
  types in `parsers/sections.py`, `parsers/world.py`, `parsers/rule.py`; stat blocks in `parsers/monster.py`; ids,
  common checks and provenance in `assemble.py`.
- The C# app runs the extractor per book (`POST /admin/books/{id}/extract-programmatic`), applies
  `books/canonical/<slug>.overrides.json` with `OverrideMerger.Apply`, writes the canonical file and ingests Accepted
  entities. `POST /admin/books/{id}/apply-overrides` re-applies overrides to the current file with
  `OverrideMerger.Reapply`.
- `OverrideMerger.ApplyOne` raises `override-drift` whenever the new extraction differs from the override's recorded
  `extracted` value — even when it now equals the override's `value`. Orphaned overrides are listed and kept.
- Precedent: `tests/data/boundary_regression.json` + `tests/test_boundary_regression.py` (section-boundary-trim) — a
  fixture of OCR'd lines plus expected text, scored by token-F1 (`boundary_score.f1`).
- The OCR cache (`<cache>/<fileHash>/ocr/NNN.json`) lives in the `book_extractor_cache` Docker volume.
- Answer key: commit 2851c5f0 added ~400 page-verified overrides and ~140 approvals (reasons "AI review vs page image",
  plus earlier hand reviews).

## Goals / Non-Goals

**Goals:**

- Every one of the six patterns has a failing test from a real review case before its fix, and passes after.
- A re-run of a fixed book retires overrides it now reproduces, instead of sending them back to review.
- The regression report gives one number per fix: how many reviewed entities now extract exactly as corrected.

**Non-Goals:**

- The coverage scan (text in no entity / two entities) — a separate later change.
- New checks that flag Accepted entities (the scan's job), the Lore review pile, a manual Decline button.
- Repairing entities from 5etools or with a model.

## Decisions

### D1. Regression fixture mirrors the boundary-regression precedent

`tests/extraction_regression_build.py` (dev script, not CI) reads every `*.overrides.json`, the canonical files and a copy
of the OCR cache, and writes `tests/data/extraction_regression.json`: per entity — id, slug, type, name, pages, OCR lines
of its pages **plus the page before and the page after** (heading context, continuations), expected `canonicalText` and
expected fields (override value as stored, else the approved extraction), and the list of overridden fields. Pages
missing from the cache are skipped and named.

*Builder's call:* the file is **append-only** — the builder merges into the committed fixture, adds new entities and never
rewrites an existing entry's expected values, so overrides that later retire stay protected. The OCR cache copy is taken
by the controller (Docker access) with `docker cp dndmcpaicsharpfun-book-extractor-1:/cache <dir>` before task 1.2;
subagents work from the committed fixture. Every pattern needs at least one real fixture case before its fix starts; if a
named case's pages are missing, a hand-made test from that page's OCR lines is allowed.
*Alternative:* store whole books — rejected (hundreds of MB; the boundary fixture is 4.7 MB for its subset).

### D2. Regression runs the real pipeline on the page subset

The service only takes a PDF today, so this change adds a small entry point that assembles entities from pre-OCR'd page
lines (the same reader → pages → parsers → assemble path, with OCR replaced by the fixture's lines), run for the entry's
type only. *Builder's call:* if the reader's text-layer path needs the PDF, the entry point feeds the fixture lines in
its place. `tests/test_extraction_regression.py` uses it, finds the entry's entity (same id, else the certain-match rule
of D5), and scores token-F1 of `canonicalText` plus a match of each overridden field. **Match** = equal after normalising
whitespace, line breaks and end-of-line hyphenation (`ob-\nject` = `object`); the same normaliser is used by D4. A committed baseline `tests/data/extraction_regression.baseline.json` stores each
entry's exact/F1 result; the gate fails on lost exact matches or an F1 drop > 0.005. A `--update-baseline` flag on the
builder rewrites the baseline after an accepted improvement, so the gate ratchets up.
*Alternative:* only full-book re-extraction — kept as the final report (D7) but too slow for the test loop.

### D3. One pattern, one rule, test first

Each pattern gets the smallest rule in the module that owns it, written against the fixture cases named in the spec:
1. *Table continuation* (`parsers/sections.py`/`rule.py`): a header line that repeats the open table's column header
   (fuzzy, digits/`d`-confusions such as `OOLP`/`Oolp` = `d100`) continues the table.
2. *Row vs heading* (`parsers/sections.py`, `names.py`): a line starting with a row number/range under an open table's
   header is a row, never a heading candidate.
3. *Stat-block end* (`parsers/monster.py`): the block ends after the last action-like entry; a heading, a size larger
   than body text, or prose without an entry lead-in ends it. Header fields stop at the first entry lead-in, so traits
   never land in `languages`.
4. *Two columns* (`layout.py`/`parsers/monster.py`): a stat block whose left column ends without the block's end
   continues at the top of the right column (then the next page).
5. *Sidebars/quotes* (`boundary.py`, which already handles shaded blocks): add all-caps/large-quote boxes and lines whose
   box lies outside the entity's column band. A headed sidebar becomes its own entity; a headingless quote box is left
   out (it stays in the book prose in `dnd_blocks`) — *builder's call, user-confirmed exemption from keep-all-content*.
6. *Sibling tables* (`parsers/sections.py`/`world.py`): a table title line (title-cased, followed by a dice header)
   starts a new entity named by the title.

Detached text (spec: keep all content) is handed to the existing section reader, which already turns headed prose into
Lore/Rule entities; no new entity type.

### D4. Three-way override outcome, in `OverrideMerger.Apply` only (user decision)

In `Apply` (fresh extraction), with `new` = the re-extracted value, `old` = the override's recorded `extracted`, and
`want` = the override's `value`:

| Case | Outcome |
| --- | --- |
| `new` matches `want` (normalised, D2) | override **retired** (dropped, counted), no drift |
| `new` equals `old` | override applied, no drift (today's behaviour) |
| `new` closer to `want` than `old` was (token-F1 for strings; JSON-normalised text for structured fields) | override **kept and applied**, no drift |
| otherwise | override applied, `override-drift` (back to review) |

So only extractions that got worse or changed sideways return to review. `Reapply` is unchanged: it runs on a file that
already holds the override values, so it cannot tell a match from an applied override. The run writes the reduced
overrides file under the per-book lock it already shares with the review page (`EntityReviewService` uses the same
`locks.AcquireAsync(slug)`). *Builder's call:* if the extraction run's status/result payload gains retired/moved/orphaned
lists and that payload is returned by an endpoint, update `DndMcpAICsharpFun.http` and `dnd-mcp-api.insomnia.json`
(CLAUDE.md rule).

### D5. Orphan migration in the extraction run

The run already has the previous canonical file before replacing it. For each orphaned override/approval: take the old
entity's type and pages; candidates are new entities of the same type whose pages include one of those pages; compare
name slugs by normalised similarity. Move only when exactly one candidate scores ≥ 0.8 and the next best is at least 0.1
lower; otherwise keep it orphaned. Moved entries keep their reason/timestamp; the `extracted` baseline is kept (D4 then
decides drift vs retire). The run result lists moved and orphaned entries.

*Builder's call:* this is meant for **renames** (one old entity ↔ one new entity). Split or merged entities (Homunculus
→ monster + infusions; XGE encounter runs → one entity per table) are not auto-moved: the margin rule leaves them
orphaned and listed, and the user handles them on the review page. Orphaned overrides stay in the file.

### D6. Order of work

1) fixture builder + baseline + gate, 2) retire-on-match + migration (C#), 3) patterns by content lost: 3 stat-block end,
2 row vs heading, 4 two columns, 6 sibling tables, 1 table continuation, 5 sidebars — each lands with its fixture case
and a baseline update, 4) re-extract the affected books.

### D7. Re-extraction report

*Builder's call:* the fixes are generic, so **all programmatic books** are re-extracted (every book with a schema-"2"
canonical file), one at a time via the existing endpoint. For each: retired / moved / orphaned overrides, the regression
totals for that book, and the review-list count before and after. **Worse** = any regression-set entity of that book
losing its match or dropping > 0.005 token-F1 (the same rule as the gate); such a book is reverted (git) and its fix
revisited. Review-pile growth from new Lore/Rule entities is accepted and reported, not bounded.

## Risks / Trade-offs

- [A rule that fixes one book shifts another] → the full fixture runs on every change; the baseline only moves up.
- [Subset extraction differs from whole-book extraction (context from far pages)] → fixture includes the previous page;
  the per-book re-extraction report (D7) is the final check.
- [New entities from detached text land in the review pile] → expected; the report lists them per book so the pile
  growth is visible, and they are real content the user wants kept.
- [Name similarity moves an override to the wrong entity] → strict margin (D5); ambiguous cases stay orphaned and listed.
- [Retiring overrides loses the record of who corrected what] → the git history of the overrides file keeps it; the run
  result counts retirements.
- [OCR cache copy needed to build the fixture] → builder documents copying the cache volume; the fixture is committed so
  tests do not need the cache.

## Migration Plan

No schema or API change. Deploy: merge, rebuild the app and book-extractor images, re-extract the affected books one at a
time (D7), commit canonical + overrides files per book. Rollback: revert the book's canonical and overrides files from git
and re-run apply-overrides.
