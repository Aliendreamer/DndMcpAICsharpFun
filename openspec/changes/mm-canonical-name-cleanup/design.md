## Context

`mm-monster-name-and-precision` #1 added `MonsterStatLineName.Strip` and applied it inside `EntityNameMatcher.Scan`,
so the matcher now resolves a stat-line-garbled heading (e.g. `ANCIENT BLACK DRAGON Gargantuan dragon, chaotic evil`)
to the clean 5etools canonical `Ancient Black Dragon`. The extraction pipeline already consumes this correctly:
`DeterministicTypeResolver.Resolve` returns the matched `CanonicalName`, and `EntityExtractionRunner.ExtractOneAsync`
overwrites BOTH the entity `displayName` and `id` from that canonical name (`EntityIdSlug.For(...)`). So a future
`force` re-extract of any book already produces clean names + clean ids — the fix is closed on the extraction path
and covered by `Match_ancient_black_dragon_statline_resolves_to_clean_name`.

The problem is purely historical data: the committed `mm14.json` (493 entities) was extracted BEFORE the fix, so it
still holds ~37 garbled grounded dragons plus their clean `5etools-backfill` duplicates (added by the recall check,
which saw the garbled name as `extra` and the clean name as `missing`). This will not recur for any future book, so
the fix belongs in a one-time migration tool, not a permanent endpoint.

Two facts make a standalone console cheap and sound: (1) `EntityNameMatcher(new EntityNameIndex(fivetoolsDir))` is
fully self-contained — the index just reads the 5etools directory synchronously in its constructor, no DI or
database; (2) `MonsterBackfillService` already stamps backfilled monsters `DataSource = "5etools-backfill"`, so
grounded vs backfilled is directly distinguishable.

## Goals / Non-Goals

**Goals:**

- Realize the #1 fix on the existing `mm14.json` in-place, deterministically, in seconds, without a re-extract.
- Guarantee cleanup output ≡ re-extract output by reusing the exact same `EntityNameMatcher` + `EntityIdSlug` code.
- De-duplicate the garbled-grounded / clean-backfill pairs, keeping the grounded record.
- Keep the transform pure and unit-tested; the console is a thin I/O wrapper.

**Non-Goals:**

- No HTTP endpoint, no `.http` / insomnia changes — one-time migration, not a product feature.
- No change to extraction, recall, backfill, or flag-unknown behavior (those already work).
- Not a general canonical linter — scope is Monster names with a stat-line suffix and their backfill duplicates.
- No deletion of grounded entities under any circumstance; no re-extract.

## Decisions

**1. One-time `Tools/CanonicalNameCleanup` console, not an endpoint or a script.**
Precedent: the retired `Tools/SqliteToPostgres` one-time migration console (referenced in CLAUDE.md). The console
reads the fivetools dir, constructs `new EntityNameMatcher(new EntityNameIndex(fivetoolsDir))`, loads the target
canonical via `CanonicalJsonLoader`, applies the pure transform, and writes back via `CanonicalJsonWriter`.
Rationale: reusing the *same* matcher + id logic is the whole soundness argument (output identical to a re-extract),
it needs no DI/HTTP, and it leaves nothing permanent behind. A raw sed/regex script was rejected — it would
duplicate stripping/slug logic and risk diverging from what a re-extract produces.

**2. Pure transform `MonsterNameCleanup.Clean(entities, matcher, bookKey) -> (entities, counts)`.**
The rewrite + de-dupe is a static, side-effect-free function over `IReadOnlyList<EntityEnvelope>`; the console only
does file I/O around it. This keeps the behavior unit-testable without touching disk. It resolves each Monster's
stored name via `matcher.MatchOfType(name, EntityType.Monster)`; when the returned canonical differs from the current
`name` (ordinal), it rewrites `name` → canonical and `id` → `EntityIdSlug.For(bookKey, Monster, canonical)`.
`bookKey` is passed in by the console, derived exactly as extraction derives it. No new stripping logic —
`MonsterStatLineName.Strip` (inside the matcher) stays the single source of truth.

**3. De-dupe by normalized name after rewriting; keep grounded, drop backfill.**
Group entities by `EntityNameIndex.Normalize(name)`. Within a colliding group: exactly one grounded (dataSource ≠
`5etools-backfill`) + one-or-more backfill → keep grounded, drop backfill(s). ≥2 grounded → keep the first (stable
order), set `NeedsReview = true` on the rest — never delete a grounded entity. This is the only place entities are
removed, and only `5etools-backfill` ones.

**4. Gap-only + idempotent.** An entity already at its clean canonical name is left unchanged; a name the matcher does
not resolve (unknown monster, no 5etools hit) is left as-is (its precision is handled later by `flag-unknown-monsters`).
Running twice is a no-op on the second pass.

**5. Report shape.** The transform returns `{ Cleaned, Deduped, GroundedCollisionsFlagged, Grounded, Backfilled }`,
mirroring `MonsterBackfillResult`'s counting style; the console prints them so before/after deltas are auditable.

## Risks / Trade-offs

- **A cleaned id collides with an unrelated existing entity's id (not a backfill duplicate)** → the normalized-name
  de-dupe catches same-name collisions; a genuine different-name id clash is not expected for monsters, and the
  post-run `POST /admin/canonical/validate` will catch any duplicate-id FAIL before commit.
- **The matcher rewrites a name it shouldn't (false clean-up)** → only rewrites when `MatchOfType(..., Monster)`
  returns a hit at the existing ≥0.90 fuzzy threshold; the strip only removes a real `<Size> <type>` suffix and never
  empties the name. Covered by the "clean names untouched" idempotency scenario and unit tests.
- **Divergence from the extraction path over time** → mitigated structurally by reusing the same matcher + id code
  rather than duplicating logic; there is no second implementation to drift.

## Migration Plan

Data steps after the console ships (superseding `mm-monster-name-and-precision` tasks 3.2/3.4/3.5/3.6):

1. Run `dotnet run --project Tools/CanonicalNameCleanup -- mm14` on the host (canonical files are host-writable) →
   dragons cleaned, backfill duplicates dropped; capture the printed counts.
2. `POST /admin/books/1/flag-unknown-monsters` → `extraUnknown` flagged (now safe: dragons are no longer garbled).
3. `GET /admin/books/1/monster-recall` → confirm 0 missing (still 450/450), improved grounded : backfilled.
4. `POST /admin/canonical/validate` → clean.
5. Commit the improved `mm14.json` + report the grounded/backfilled/extra deltas.

Rollback: `git checkout books/canonical/mm14.json` restores the committed 493-entity canonical; the console is a
one-time, stateless tool.
