## Why

The `mm-monster-recall` change took Monster Manual monster recall to 450/450, but a deep-dive on the
result found three quality issues, all evidenced in the live canonical:

1. **Stat-line name garbling (the big one).** ~37 dragons (and e.g. Animated Armor) extracted correctly
   and GROUNDED — `ANCIENT BLACK DRAGON Gargantuan dragon, chaotic evil` has `hp=367`, a real stat block —
   but the extractor kept the trailing stat line (`"<Size> <type>, <alignment>"`) as part of the NAME.
   `EntityNameMatcher` then fails to match that against the 5etools `Ancient Black Dragon` (the extra words
   sink the fuzzy score below 0.90), so no canonical name is assigned, the name stays garbled, and the
   clean-named dragon is reported `missing` and **backfilled** — the same dragon ends up in the canonical
   TWICE (grounded-misnamed + backfilled-clean). This inflates both `extra` (~37) and `backfilled` (~37).
2. **False-positive monsters (precision).** Some `extra` entries are genuine bad extractions, not roster
   gaps: `Lord Soth` (a Dragonlance NPC, not in the 2014 MM) with a nonsense `hp=11`; `Roa` (an OCR
   fragment) with a stat block. These are not in the 5etools bestiary at ALL. (The demon lords —
   Orcus/Demogorgon/Baphomet — are a separate case: they exist in 5etools but under `MPMM`/`MTF`, not
   `MM`, so the `source==MM` roster correctly excludes them.)
3. **Failed grounded candidates.** 11 candidates (long dragon/legendary stat blocks) failed extraction and
   fell to backfill; a cheap `errorsOnly` retry can recover some of them grounded.

Scope: Monster Manual monsters (continuation of the recall work). DMG / other types remain the later pass.

## What Changes

- **Stat-line name stripping (#1).** Add a deterministic stat-line-suffix stripper (drop a trailing
  `"<Size> <creature-type>[, <alignment>...]"` where Size ∈ Tiny/Small/Medium/Large/Huge/Gargantuan and
  creature-type is one of the 14 D&D types + swarm) and apply it in `EntityNameMatcher.Match`/`MatchOfType`
  BEFORE `EntityNameIndex.Normalize`. A garbled heading then matches its 5etools entry, so
  `DeterministicTypeResolver` assigns the clean canonical name/id — the dragon extracts grounded with the
  correct name, is no longer reported missing, and is not backfilled as a duplicate. Conservative: only
  strips a clear stat-line SUFFIX after real name text; never empties the name.
- **Extra categorization + precision flag (#2).** The monster recall check splits `extra` into
  `extraOtherSource` (present in 5etools under a different source — informational, e.g. demon lords) and
  `extraUnknown` (not in the 5etools bestiary at all — likely false positives). Add an operation to mark
  the `extraUnknown` monsters `NeedsReview` in the canonical (soft, reversible — never auto-deletes a
  possibly-legit monster).
- **errorsOnly retry (#3).** Documented/scripted as a verification step: after #1 lands and MM is
  re-extracted, `POST /admin/books/{id}/extract-entities?errorsOnly=true` retries the failed candidates to
  recover more grounded monsters before backfill.

## Capabilities

### New Capabilities

- `monster-name-normalization`: strip the stat-line suffix from monster headings so stat-line-garbled
  names (dragons, animated armor) resolve to their clean 5etools canonical names during matching/recovery.
- `monster-precision-flagging`: categorize recall-check `extra` monsters (other-source vs not-in-5etools)
  and flag the not-in-5etools ones `NeedsReview`.

## Impact

- Modified: `Features/Ingestion/EntityExtraction/EntityNameMatcher.cs` (+ a `StatLineStripper` helper),
  and the monster recall/backfill service (`MonsterBackfillService` / recall result) for the `extra`
  split + flag. New admin op for the precision flag (endpoint → `.http`/insomnia).
- Data: re-extract MM to apply #1 (produces clean dragon names, fewer duplicates); then `errorsOnly` +
  `backfill-monsters`. No schema/migration change.
- No change to the extraction LLM step, type resolution logic, or non-monster types.
