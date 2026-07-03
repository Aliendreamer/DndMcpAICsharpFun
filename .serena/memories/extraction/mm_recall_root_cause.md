# Monster Manual recall — root cause + FIX (mm-monster-recall change), 2026-07-02/03

Goal: core 2014 books (PHB/MM/DMG) to PHB-quality entity recall, then 2024 books. PHB (2026-07-01,
post-recall-fix) good. MM/DMG were extracted 2026-06-27 pre-fix. **A live `force` re-extract proved the gap
is UPSTREAM of extraction** (not the `extraction-name-resolution` fix): MM classifies 100% TOC `Rule`
(699 Rule/3 Unknown/0 Monster), so `EntityCandidateScanner` skipped 449 monster sections. Missing Aboleth+
Beholder. `BookType`=Core for all three (no bestiary flag).

## FIX = openspec change `mm-monster-recall` (brainstormed->propose->build, MONSTER-ONLY, MM-FIRST; DMG later)
Design decisions (user): success anchored to the 5etools MM roster (A); approach = 5etools recovery for
official books + structural fallback (C); completeness = extraction + 5etools backfill (A, like PHB spells).

**Code shipped (commits c301bfb T1, 0104f1f T2/T3; 872 tests green):**
- **T1 candidate recovery** (`EntityCandidateScanner.Scan` now takes matcher+recoverMonsters+ungateOnTocFailure;
  `EntityCandidateBuilder` passes `_matcher` + `recoverMonsters = FivetoolsSourceKey is not null`): in the
  `type is null` skip branch, `matcher.MatchOfType(section, Monster)` recovers the section as a Monster
  candidate. Non-official fallback: ungate when stat blocks exist but 0 entity-mappable TOC pages. Stat-block
  scanner already TOC-independent.
- **T2/T3 `MonsterBackfillService`** (mirrors `SpellBackfillService`): reads `5etools/bestiary/bestiary-*.json`
  (prop `monster`, source==key), diffs canonical Monster names → `{missing,extra,grounded,backfilled}`;
  builds missing as EntityEnvelope with full stat-block Fields projected from 5etools JSON (round-trips as
  MonsterFields), `dataSource:"5etools-backfill"`, gap-only+idempotent. Endpoints:
  `GET /admin/books/{id}/monster-recall` (diff, no write) + `POST /admin/books/{id}/backfill-monsters`.

## Live validation (in progress 2026-07-03)
BASELINE recall (old mm14.json vs 5etools MM roster, ~450 monsters): **156 present / 294 MISSING / 0
backfilled**, 61 extra. Re-extract with recovery: **227 monsters recovered**, candidates **258 -> 392**;
iconics back grounded (Aboleth/Aarakocra/Androsphinx/Mind Flayer/Owlbear/Tarrasque); 475 non-monster
sections correctly still skipped (precision held). Full grounded extraction (~3h over 392 candidates)
RUNNING; then `monster-recall` to measure grounded recall, then `backfill-monsters` to close residual ->
target ~100%. TASK 4.x of the change open until this completes + validate + (optional) ingest-entities.
DMG + other gated types = explicit follow-up. Relevant code in `Features/Ingestion/EntityExtraction/` +
`FivetoolsIngestion/MonsterBackfillService.cs`. See `mem:project_companion_roadmap`,
`mem:operations/running_the_stack`.
