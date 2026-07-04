# WHERE WE ARE — Monster Manual entity recall/quality (2026-07-03, COMPLETE except dnd_entities re-ingest)

Goal: core 2014 books (PHB/MM/DMG) to PHB-quality entity recall, then 2024. **MM is DONE (recall +
name-precision + in-place data cleanup all shipped, committed, ARCHIVED); DMG is next.**

## Shipped + ARCHIVED (all three, 2026-07-03)
1. `mm-monster-recall` (73c291d) — MM 156/450 → 450/450, 0 missing. Recovery + 5etools backfill.
2. `mm-monster-name-and-precision` — stat-line strip (#1, closes the extraction path: `ExtractOneAsync`
   rewrites name+id from the matcher's canonical), extra split + `flag-unknown-monsters` (#2), errorsOnly (#3).
3. `mm-canonical-name-cleanup` (transform 7629ac1, dup-id fix 168865c, console 92e99c6, DATA e5d4d59) —
   ONE-TIME in-place cleanup (NO endpoint; `Tools/CanonicalNameCleanup`). `MonsterNameCleanup.Clean` reuses
   `EntityNameMatcher.MatchOfType` + `EntityIdSlug.For` so output ≡ a re-extract. Winner holds canonical id,
   losers keep ORIGINAL id+name + NeedsReview (distinct ids). Archived as 2026-07-03-*.

## LIVE RUN RESULT (commit e5d4d59, real books/canonical/mm14.json)
console: cleaned 16, deduped 16, groundedCollisionsFlagged 7 → 493→477 entities, **0 duplicate ids**.
flag-unknown-monsters flagged 4 more. monster-recall: **present 450, missing 0 (still 450/450)**, grounded
337 : backfilled 136 (was 337:152). canonical/validate: **0 failures for mm14** (corpus HTTP 422 = pre-existing
dangling-cross-ref warnings in `system-reference-document.json` only; endpoint 422s on NeedsReview.Count>0 too).

## CRITICAL LESSON (folded into dev-flow skill)
The grounded-vs-grounded collision path first wrote the loser at the SAME canonical id as the winner → 6
duplicate ids → `CanonicalJsonLoader` THROWS → file un-ingestable. FOUR per-task-green reviews missed it;
the whole-branch review + a real-data reload caught it. Rule: any in-place canonical rewrite MUST end with a
unique-id invariant (`ids.OnlyHaveUniqueItems()`) + a load round-trip before trusting. Also: one-time
migration = `Tools/` console, NOT a permanent endpoint. dev-flow SKILL.md updated (commit 0c7dca1).

## ⚠️ DEFERRED — dnd_entities re-ingest (finishing step 5 `ingest-entities`)
`POST /admin/books/1/ingest-entities` returns 202 but the background job FAILS: **Ollama is unreachable**
(entity ingestion embeds canonicalText via Ollama). Ollama container `personalcommandcenter-ollama-1`
(a DIFFERENT compose project) Exited (128) ~11h ago, not on :11434. TO FINISH: start Ollama
(`docker start personalcommandcenter-ollama-1`), then re-run `ingest-entities` so `dnd_entities` reflects
the cleaned names/dropped dupes. The canonical (source of truth) is already clean+committed; this only
re-projects into Qdrant. The app container "unhealthy" flag is ONLY this Ollama probe — admin/canonical
endpoints work without it.

### Known residuals (flagged NeedsReview for human review — NOT bugs)
- Collision losers keep garbled names + NeedsReview (ANIMATED ARMOR, BLACK/GELATINOUS/PSEUDODRAGON/REVENANT …).
- `DRAGON TURTLE Gargantuan'dragon, neutral`: OCR apostrophe defeats the `\s+<Size>\s+<type>` stripper →
  not cleaned, but flag-unknown flagged it. To catch this class, widen the separator to `[\s']+`. Low priority.

## State of the machine
Working tree CLEAN. `books/` chowned back to host uid 1000. Admin auth = header `X-Admin-Api-Key`
(value=`Admin__ApiKey`, git-crypt'd; DON'T extract to output). 899 unit tests green, build 0/0.

## NEXT
- Start Ollama + re-run `ingest-entities` to finish MM (deferred above).
- DMG: generalize recovery+backfill to all gated types (MagicItem-heavy). See `mem:companion_roadmap`.
