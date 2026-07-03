## Context

`EntityNameMatcher.Match(rawName)` normalizes via `EntityNameIndex.Normalize` (keep alphanumerics,
lowercase) and looks the result up in the 5etools index. `EntityNameNormalizer.TryNormalizeHeading` only
title-cases pure all-caps names, so a mixed-case garbled heading (`ANCIENT BLACK DRAGON Gargantuan dragon,
chaotic evil`) is left untouched and normalizes to `ancientblackdragongargantuandragonchaoticevil`, which
does not match `ancientblackdragon`. `DeterministicTypeResolver`/`EntityExtractionRunner` only assign the
clean 5etools canonical name when the matcher resolves a `ForceType` + `CanonicalName`; on a miss, the
garbled heading survives as the entity name. Downstream, `MonsterBackfillService` diffs by `Normalize`, so
a garbled-but-grounded dragon is reported missing → backfilled (a duplicate). `Normalize` collapses
whitespace, so the stat-line must be stripped on the RAW name before normalization.

## Goals / Non-Goals

**Goals:**
- Stat-line-garbled monster headings resolve to their clean 5etools canonical name → grounded, correctly
  named, not duplicated by backfill.
- Distinguish genuine false-positive monsters (not in 5etools at all) from cross-source ones, and flag the
  former for review without destroying possibly-legit data.
- Recover more grounded monsters from the failed candidates before backfill.

**Non-Goals:**
- Non-monster types / DMG (later pass).
- Auto-deleting any extracted entity.
- Improving MinerU OCR; changing the LLM extraction or type-resolution logic.

## Decisions

1. **Stat-line stripper (#1).** New deterministic helper, e.g. `MonsterStatLineName.Strip(string) ->
   string`: regex (case-insensitive) matching a SUFFIX
   `\s+(Tiny|Small|Medium|Large|Huge|Gargantuan)\s+(aberration|beast|celestial|construct|dragon|elemental|fey|fiend|giant|humanoid|monstrosity|ooze|plant|undead|swarm)\b.*$`
   → remove and trim. Guards: require non-empty name text before the match (never return empty); leave the
   string unchanged if no match. Apply it inside `EntityNameMatcher.Match`/`MatchOfType` to `rawName`
   BEFORE `Normalize`. This fixes matching for BOTH type resolution (→ clean canonical name/id via the
   existing `ForceType`+`CanonicalName` path) and candidate recovery (`MatchOfType`), with a single change.
   The 5etools roster names have no stat lines, so their keys are unaffected (safe no-op on clean names).
   Names like "Dragon Turtle" (no `<Size> <type>` suffix) and "Giant Ape" (size word not followed by a
   creature type) are NOT stripped.
2. **Extra categorization (#2).** In the monster recall result, for each `extra` (canonical monster whose
   normalized name is not in the book's `source==MM` roster), check the FULL cross-source 5etools index
   (via `EntityNameMatcher`/`EntityNameIndex`): if it matches a Monster of another source →
   `extraOtherSource`; if it matches no 5etools monster → `extraUnknown`. Report both.
3. **Precision flag (#2).** An admin operation marks each `extraUnknown` monster in the canonical
   `NeedsReview = true` (soft, reversible; the file is rewritten with the flag, entity preserved). Never
   deletes. `extraOtherSource` monsters are left as-is (they are plausibly real, just cross-printed).
4. **errorsOnly retry (#3).** No new code — a verification step: `extract-entities?errorsOnly=true` re-runs
   only the `errors.json` candidates. Documented in tasks; run after #1's full re-extract, before backfill.

## Risks / Trade-offs

- **Over-stripping a real name.** Mitigated by requiring the exact `<Size> <creature-type>` two-token
  pattern as a suffix after real name text, plus the never-empty guard; covered by unit tests including
  negative cases (Dragon Turtle, Giant Ape, plain names).
- **Flagging a legit monster as NeedsReview.** `extraUnknown` is conservative (soft flag, not deletion) and
  only fires when the name matches NO 5etools monster at all — the population is dominated by OCR garbage
  (`Roa`) and cross-book contamination (`Lord Soth`). A human clears false flags in review.
- **Re-extract cost.** #1 requires re-running MM extraction (~hours) to take effect on the canonical; the
  matcher/unit tests verify correctness cheaply first, and the live run is the acceptance gate.
