## Why

The stat-line-strip fix (`mm-monster-name-and-precision` #1) is code-only: the committed `books/canonical/mm14.json`
still holds ~37 monsters (mostly dragons) whose names are stat-line-garbled (e.g. `ANCIENT BLACK DRAGON Gargantuan
dragon, chaotic evil`), each shadowed by a duplicate clean `5etools-backfill` entity that the recall check added
because it saw the garbled name as `extra` and the clean name as `missing`. The extraction path itself is already
fixed (every future extract is clean), so this is a one-time cleanup of legacy pre-fix data — it does not warrant a
permanent API surface, and the only alternative the existing change offers (an ~8h `force` re-extract) is slow,
LLM-nondeterministic, and re-touches every entity.

## What Changes

- Add a one-time developer console `Tools/CanonicalNameCleanup` (same pattern as the retired `Tools/SqliteToPostgres`
  one-time migration) that rewrites a book's stat-line-garbled canonical Monster names to their clean 5etools form and
  de-duplicates the resulting duplicate backfills, in place.
- The name/id rewrite reuses the SAME `EntityNameMatcher` + `EntityIdSlug` the extraction pipeline uses (matcher
  constructed directly from the 5etools directory — no DI, no database), so the console's output is identical to what
  a re-extract would produce. De-dupe keeps the grounded entity and drops the `5etools-backfill` duplicate; a rare
  grounded-vs-grounded collision keeps the first and flags the other `NeedsReview` (never deletes a grounded entity).
- Factor the rewrite+de-dupe as a pure, unit-tested transform so the console is a thin I/O wrapper.
- No HTTP endpoint, no `.http` / insomnia changes.
- **Supersedes** the re-extract-based data tasks (3.2/3.4/3.5/3.6) of `mm-monster-name-and-precision`: the MM data
  cleanup is realized by running this console once, then `flag-unknown-monsters` + recall re-check + validate.

## Capabilities

### New Capabilities

- `monster-name-cleanup`: a one-time in-place transform (invoked via a dev console) that rewrites stat-line-garbled
  canonical monster names to their clean 5etools canonical form (reusing the extractor's matcher + id logic) and
  de-duplicates the resulting duplicate backfills, without a re-extract and without deleting grounded entities.

### Modified Capabilities

<!-- None: the extraction-path behavior in monster-name-normalization is unchanged; this change adds a one-time
     data-cleanup transform that reaches the same end state without re-extraction. -->

## Impact

- New console project `Tools/CanonicalNameCleanup` (added to the solution) + a pure transform type it calls, reusing
  `EntityNameMatcher`, `EntityNameIndex`, `EntityIdSlug`, `CanonicalJsonLoader`, `CanonicalJsonWriter`.
- Data: `books/canonical/mm14.json` rewritten (garbled dragon names cleaned, duplicate backfills removed) — improves
  the grounded : backfilled ratio (was 337 : 152) with recall still 450/450.
- No API/docs changes.
