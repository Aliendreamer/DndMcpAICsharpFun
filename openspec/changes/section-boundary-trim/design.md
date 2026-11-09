## Context

The programmatic extractor (`Tools/BookExtractor`, no LLM) builds world/rule sections from OCR lines (box, height, score, page). `parsers/sections.py` (`_ends`, `_read`, `drop_art`, `drop_pull_quotes`, `foreign_lines`) and `parsers/world.py` (`_doubt`, `_doubtful_tail`, `_text_check`) already compute heading sizes and already flag many over-long sections as doubtful — which is why they sit in NeedsReview.

Evidence (spike, 2026-11-09, scripts in the session scratchpad `boundary_spike/`):

- 258 sections checked against page images by ChatGPT: 199 FIXED, 59 MATCH. About 85 FIXED were pure trims (our text = correct words + extra). The rest were OCR typos or table re-flow — not boundary problems.
- Per-line signals available: OCR box, line height, score, page. No font style, no block type. Line-height ratio alone is useless; pixel shade alone is weak; heading size + position is the strongest signal.
- Prototype on 60 trim + 40 MATCH entries: heading cut + shaded-sidebar + map-label improved 30/60 (F1 ≥ 0.95 went 0 → 19), 2 worse (one catastrophic: `erlw.lore.the-dreaming-dark-dreaming-dark-p198-2`, anchor matched a continuation-page heading, 0.77 → 0.02), 2/40 MATCH lost a legitimate sub-heading (F1 0.97/0.98).
- Thresholds were tuned on the same sample, so these numbers are optimistic. The spike re-ran RapidOCR because the Docker OCR cache was unreachable; production must use the real cache.

Owner decisions (2026-11-09): trims are applied **and approved automatically**; the goal is a smaller review pile; BGG is included (owner, 2026-11-09); extract-once rule holds — existing books are not re-extracted.

## Goals / Non-Goals

**Goals:**

- End sections at their true boundary for the heading, shaded-sidebar and map-label cases.
- Shrink the NeedsReview pile of the 16 extracted books without touching approved entities.
- Same rules for future extractions.
- Never silently approve a destructive cut.

**Non-Goals:**

- Captions and pull-quotes (not separable without font/block-type data).
- OCR typo fixes and table re-flow.
- Re-extracting any existing book, or changing the SRD.
- A layout model or new dependency.

**Anti-goal:** an auto-approved entity whose text lost real content. One bad approval is worse than leaving ten entries flagged.

## Decisions

1. **Rules live once, in the extractor.** The heading cut, shaded-sidebar and map-label rules are functions in `parsers/sections.py` (shared by world and rule parsers), reusing the sizes `_ends` already computes rather than the spike's duplicate machinery. *Alternative:* a separate post-processor over text — rejected, it loses line geometry.
2. **Heading cut, guarded.** Anchor on the entity's own heading line (matched to its name/slug); a later line is a cut point when its height ≥ 0.9 × the anchor heading and ≥ 1.15 × median body height, in the same column or on a later page. The anchor must be on the section's first page; if no anchor is found there, no cut is made. The cut point must not be a heading that the existing logic already treats as a child/sub-heading of the section. *Why:* the naive "first big heading" version broke 8 FIXED and 2 MATCH; the catastrophic spike failure was a continuation-page anchor.
3. **Shade from rendered pixels.** Render the page with pymupdf on CPU and compare the background around a line's box with the page's paper colour. A block is dropped only when it is shaded **and** opens with its own heading line, and does not follow a `read:`-style cue (read-aloud boxes).
4. **Trim pass = dev tool mirroring `fivetools_fix`.** `python -m bookextractor.boundary_fix --canonical books/canonical --books … [--dry-run] [--report out.json]`. For each NeedsReview Lore/Rule (and other world-section) entity outside `SKIP_BOOKS = {system-reference-document, dragonborn-slice}`, it loads the book's cached OCR pages for the entity's provenance pages, rebuilds the section's lines, applies the rules, and if the text changes writes an override (`entityId`, `field`, `value`, `extracted`) for the text field(s) the entity uses, in the same shape the existing overrides use. Entities already carrying an override on that field or already approved are skipped. Applied through the existing `POST /admin/books/{id}/apply-overrides`.
5. **Auto-approve gate.** Approve (approval `reason: "boundary trim"`, waiving only `text-complete`, `neighbour-absorbed` and `entry-level`) only when **all** hold: no other check still fails; kept tokens ≥ 50% of the original; the cut starts at a line the rules identified (heading/shaded block/map label), never mid-paragraph; the kept text is non-empty and still begins with the entity's own heading/name. Otherwise the override is written but the entity stays NeedsReview. The 50% cutoff is Claude's call and is re-checked against the regression set before the real run.
6. **Regression set gates the run.** The 258 ChatGPT answers (copied into `Tools/BookExtractor/tests/data/` as a compact fixture: id, our text, corrected text, verdict) are the acceptance test: no MATCH entity may drop below token-F1 0.95 vs its original, and no auto-approved FIXED entity may score worse than before.

## Risks / Trade-offs

- [Thresholds overfit the spike sample] → re-measure on the full 258-entry set with production OCR before the run; report numbers to the owner.
- [A legitimate sub-heading of equal size is cut (2/40 MATCH in spike)] → sub-heading exclusion in Decision 2; 50% keep gate stops large losses from being approved; trimmed text is kept in `extracted` so it is reversible.
- [Auto-approval hides a bad trim] → overrides are per-entity, committed per book, and reversible by deleting the override; the trim pass report lists every approval for spot-checking.
- [Extraction-time change alters future books' output] → existing books are never re-extracted; extractor tests must stay green.

## Migration Plan

1. Land rules + tests; run the regression set; report numbers.
2. `boundary_fix --dry-run` across the 16 books; report counts (trimmed / approved / left flagged) per book.
3. Real run, apply-overrides per book, commit overrides + book files per book (BGG included; its pre-existing uncommitted changes are inspected and settled with the owner first).
Rollback: revert the commit(s), or delete the `boundary trim` overrides/approvals and re-apply.

## Settled details (builder check, 2026-11-09 — all Claude's calls unless marked)

- **Fields written.** Every trim writes two overrides, `fields.entries` and `canonicalText`, the same pair the 1,775 existing Lore overrides use. `canonicalText` is what `dnd_entities` embeds, so both must change together. The `entries` list keeps the original paragraph boundaries and is cut at the paragraph or line where the cut starts; `canonicalText` is the existing title-plus-joined-entries rendering of the trimmed list.
- **Trim = stored text minus cut lines.** The pass does **not** require a rebuild to reproduce the stored text byte for byte. It rebuilds the section's OCR lines only to find the cut lines (sibling heading, shaded block, map label), then removes the matching text from the stored `entries`. If a cut line's text cannot be located unambiguously in the stored text, the entity is skipped and left as it is.
- **OCR cache access.** The production cache is in the `book_extractor_cache` Docker volume, keyed by PDF sha (`<cache>/<sha>/ocr/NNN.json`, read by `BookReader`); PDFs are under `books/<hash>.pdf`. Copy the cache out of the volume to a host scratch directory once (`docker cp`), and run the pass on the host against it. A missing page in the copied cache skips that entity; nothing is re-OCRed during the pass.
- **Approval and waived checks.** The pass may waive only the boundary doubts a trim resolves: `text-complete`, `neighbour-absorbed`, and `entry-level` (owner, 2026-11-09, after the Task 4 numbers: approvals 4 → ~13–17 on the regression set, none worse). The opening check compares the opening tokens of the first entry, not the whole first entry, so a trim inside paragraph one still counts as keeping the opening. An entity is approved only if, after the trim, it has **no other failing check** (for example `entry-level`, `type-from-context` or `name-words-known` still failing means the override is written but the entity stays flagged). This leans toward fewer approvals, in line with the anti-goal.
- **Types in scope.** Only entities built by the world and rule section parsers (`parsers/world.py`, `parsers/rule.py`): Lore, Rule, God, Plane, Faction and Location. Stat-block and character types are out of scope.
- **Own-heading anchor.** Match on a normalized name: casefolded letters and digits only, with any `-pNNN`/`-N` id suffix ignored, and the OCR line counted as the heading when its similarity is at least 0.85. Its page must be the entity's first provenance page. Zero matches, or more than one match on that page, means no cut, and the entity is left unmodified.
- **Regression fixture.** Copy the 258 answers into `Tools/BookExtractor/tests/data/` together with the OCR line data for their pages, taken from the copied cache. The tests then need no Docker or cache. If the thresholds cannot meet both gates on the full set, the builder stops and reports the numbers to the owner rather than retuning past the spike values.
- **Shade.** Start from the spike's measure, the mean background darkness around the line box compared with the page paper, with a threshold of about 15 on 0–255. Render on the CPU at about 100 DPI and measure paper per page, so tinted books calibrate themselves. "Opens with its own heading" uses the same heading-height test as the heading cut. Read-aloud cues are a line ending in `read:` or `reads:`. The builder may tune these against the fixture.
- **Extraction time.** During extraction the rules only change the text. They never approve, and existing checks run on the trimmed text as they do today.
- **Files ignored.** `.bak` files and anything that is not `<book>.json` / `<book>.overrides.json` are ignored. The skip set is `system-reference-document` and `dragonborn-slice`; BGG is in scope (owner).
