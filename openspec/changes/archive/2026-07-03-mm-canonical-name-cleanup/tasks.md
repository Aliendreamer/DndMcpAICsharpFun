## 1. Pure cleanup transform (TDD)

- [x] 1.1 Add `MonsterNameCleanup` static class (in `Features/Ingestion/FivetoolsIngestion/`) with `Clean(IReadOnlyList<EntityEnvelope> entities, EntityNameMatcher matcher, string bookKey) -> (IReadOnlyList<EntityEnvelope> Entities, MonsterNameCleanupCounts Counts)` where counts = `{ Cleaned, Deduped, GroundedCollisionsFlagged, Grounded, Backfilled }`. Stub + counts type first.
- [x] 1.2 Unit test (RED): a grounded entity named `ANCIENT BLACK DRAGON Gargantuan dragon, chaotic evil` → after `Clean`, `name == "Ancient Black Dragon"`, `id == EntityIdSlug.For(bookKey, Monster, "Ancient Black Dragon")`, stat-block `fields` + `dataSource` preserved; `Counts.Cleaned == 1`.
- [x] 1.3 Implement 1.2: iterate Monster entities, `matcher.MatchOfType(name, Monster)`; when the canonical differs (ordinal) rewrite `name` + recompute `id`; preserve every other field. Non-Monster entities and unmatched names untouched. GREEN.
- [x] 1.4 Unit test + impl (de-dupe): garbled grounded dragon + a separate `5etools-backfill` `Ancient Black Dragon` → single grounded `Ancient Black Dragon` remains, backfill dropped, `Counts.Deduped == 1`. Group by `EntityNameIndex.Normalize`; keep grounded, drop backfill; never delete grounded.
- [x] 1.5 Unit test + impl (grounded-vs-grounded collision): two grounded resolve to the same clean name → keep first, other retained with `NeedsReview == true`, `Counts.GroundedCollisionsFlagged == 1`, no deletion.
- [x] 1.6 Unit test (idempotency/no-op): clean names + non-monsters → all-zero counts and identical entity list; feeding the output back through `Clean` yields no further changes.

## 2. One-time console

- [x] 2.1 Add `Tools/CanonicalNameCleanup` console project (net10.0, added to the solution; mirror `Tools/SqliteToPostgres`/`Tools/SchemaGenerator` csproj style). Arg: canonical slug (e.g. `mm14`). Resolve the 5etools dir + canonical dir from config/relative paths.
- [x] 2.2 Console body: build `new EntityNameMatcher(new EntityNameIndex(fivetoolsDir))`, load `<slug>.json` via `CanonicalJsonLoader`, derive `bookKey` from the slug the same way extraction does, call `MonsterNameCleanup.Clean`, write back via `CanonicalJsonWriter`, and print the counts (cleaned / deduped / groundedCollisionsFlagged / grounded / backfilled).

## 3. Verify code

- [x] 3.1 `dotnet build` (warnings-as-errors) + `dotnet test` green (sandbox disabled per git-crypt; Docker up for persistence tests).

## 4. Data cleanup run (MM) — supersedes mm-monster-name-and-precision 3.2/3.4/3.5/3.6

- [x] 4.1 `dotnet run --project Tools/CanonicalNameCleanup -- mm14` on the host → capture report. Spot-check `mm14.json`: `Ancient Black Dragon` present once, grounded, clean id; no `Gargantuan dragon` garbled names remain.
- [x] 4.2 Ensure the app container serves the current image, then `POST /admin/books/1/flag-unknown-monsters` → `extraUnknown` (Lord Soth, Roa, …) flagged `NeedsReview`; `extraOtherSource` untouched.
- [x] 4.3 `GET /admin/books/1/monster-recall` → confirm 0 missing (still 450/450) and record the improved grounded : backfilled ratio vs 337 : 152.
- [x] 4.4 `POST /admin/canonical/validate` → clean (no duplicate-id FAIL).
- [x] 4.5 Commit the improved `books/canonical/mm14.json` + report before/after (grounded/backfilled/extra) deltas. Update the Serena `extraction/mm_monster_status` memory.
