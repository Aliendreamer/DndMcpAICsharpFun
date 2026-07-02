## Context

Candidate generation for a book runs in `EntityCandidateBuilder`: MinerU structure items →
`BookmarkTocMapper.Map(bookmarks)` → `TocCategoryMap` (page → `ContentCategory`) → the section
scanner (`EntityCandidateScanner`) which **skips** a section when its page's category is not
entity-eligible, plus a stat-block scanner that detects `Armor Class/Hit Points/Speed` structure.
`HeadingCategoryClassifier.Guess` keys on category keywords ("monster", "dragon", …), so individual
monster-name headings (`ABOLETH`) fall through to `ContentCategory.Rule`. In the Monster Manual every
page classifies `Rule` (0 Monster pages), so the section path yields nothing and 449 real-monster
sections are skipped; only the stat-block path survives (258 candidates, missing OCR-damaged iconics).

The pipeline already: (a) matches candidate names against the 5etools index via `EntityNameMatcher` /
`EntityNameIndex` for type resolution (the `extraction-name-resolution` fix), and (b) has a
deterministic 5etools gap-fill precedent in `SpellBackfillService` + `POST .../backfill-spells`
(`dataSource:"5etools-backfill"`), which took PHB to 361/361 spells. This change reuses both.

## Goals / Non-Goals

**Goals:**
- Monster Manual reaches ~100% monster recall measured against the authoritative 5etools MM roster.
- The bulk of monsters are **grounded** (extracted from PDF prose), with 5etools backfill only
  closing the residual the PDF cannot yield.
- Recovery raises recall without lowering precision (non-monsters are declined, not fabricated).
- A reusable recall oracle (extracted-vs-5etools-roster diff) that also drives backfill.

**Non-Goals:**
- Non-monster gated types (MagicItem/Spell/Class/…) and DMG — explicit later pass.
- Changing the qwen3 extraction step, the type-resolution logic, or the block/BM25 layer.
- Homebrew/non-5etools candidate recovery beyond the structural fallback below.
- Improving MinerU OCR quality itself (only ~20 losses; out of scope).

## Decisions

1. **5etools-roster candidate recovery (official books).** In `EntityCandidateBuilder`, when a
   section would be skipped on TOC-category grounds AND the book has a `fivetoolsSourceKey`,
   fuzzy-match the section heading against the 5etools **monster** names via the existing
   `EntityNameMatcher`/`EntityNameIndex` at the current 0.90 confidence bar. A confident match
   recovers it as a `Monster` candidate (carrying the matched canonical name). No match → falls
   through to the structural rules below. Recovery never *drops* — a non-match degrades to the
   existing behavior, so it cannot regress precision.
2. **Authoritative stat-block scanner.** A detected stat block (`Armor Class/Hit Points/Speed`
   structure) is always emitted as a candidate regardless of TOC category. A stat block is a
   definitive creature signal.
3. **TOC-failure ungate.** When a book yields stat blocks but **zero** Monster TOC pages, treat TOC
   categorization as failed and stop letting the TOC gate suppress its sections; rely on the
   extraction **decline gate** as the filter instead of the (broken) TOC gate.
4. **Recall check = 5etools roster diff.** `MonsterRecallService` loads the authoritative MM monster
   roster from the local 5etools bestiary filtered to `source == MM` (monsters are cross-source
   attributed in 5etools, so filter by source, not by matching the whole corpus), normalizes names,
   and diffs against the extracted canonical's monster entities → `{missing[], extra[]}`. Exposed as
   an admin endpoint; consumed by backfill.
5. **Monster backfill mirrors spell backfill.** `MonsterBackfillService` +
   `POST /admin/books/{id}/backfill-monsters`: for each roster monster missing from the canonical,
   project the 5etools monster (via the existing `FivetoolsMapperRegistry` monster mapper) into a
   canonical entity marked `dataSource:"5etools-backfill"`, appended gap-only and idempotently.
   Same `?force`-re-extract caveat as spells: a `force` re-extract overwrites the canonical, so
   backfill is re-run after.

## Risks / Trade-offs

- **Ungating adds extraction cost.** Recovered/ungated sections become qwen3 candidates → longer
  runs and a larger `declined.json`. Acceptable: this is a single-user local tool where latency is
  a non-issue, and the decline gate keeps output correct.
- **Fuzzy-match false positives.** A heading could match the wrong monster. Mitigated by the
  established 0.90 confidence bar and by recovery only *adding* a candidate (extraction + decline
  still adjudicate the final entity).
- **Backfilled monsters are not prose-grounded.** They carry `dataSource:"5etools-backfill"` and are
  the minority (residual only), matching the accepted PHB-spell precedent; the recall check reports
  the grounded-vs-backfilled split.
- **5etools MM roster completeness/attribution.** Filtering by `source == MM` must match how the
  bestiary attributes monsters; validated by the live MM run (Aboleth/Beholder must appear).
