# Monster Manual recall — root cause, FIX, and RESULT (mm-monster-recall), 2026-07-03 ✅ DONE

Goal: core 2014 books (PHB/MM/DMG) to PHB-quality entity recall, then 2024. MM/DMG were extracted 2026-06-27
pre-recall-fix. **The gap was UPSTREAM of extraction** (not `extraction-name-resolution`): MM classifies 100%
TOC `Rule` (0 Monster pages) because per-monster headings are individual names (`ABOLETH`), so
`EntityCandidateScanner` skipped 449 monster sections. `BookType`=Core for all three (no bestiary flag).

## FIX = openspec change `mm-monster-recall` (monster-only, MM-first; DMG + other types later)
User design calls: success anchored to 5etools MM roster (A); 5etools recovery for official + structural
fallback (C); extraction + 5etools backfill for completeness (A, like PHB spells).
- **T1 candidate recovery** (`c301bfb`): `EntityCandidateScanner.Scan` recovers a TOC-skipped section as a
  Monster candidate when its heading matches a 5etools Monster (`EntityNameMatcher.MatchOfType`), for official
  books (`FivetoolsSourceKey != null`); non-official ungate fallback. Stat-block scanner already TOC-independent.
- **T2/T3 `MonsterBackfillService`** (`0104f1f`): mirrors SpellBackfillService — `GET
  /admin/books/{id}/monster-recall` (diff canonical Monster names vs 5etools bestiary source==key →
  present/missing/extra/grounded/backfilled) + `POST .../backfill-monsters` (append missing as EntityEnvelopes
  with full stat-block Fields from 5etools JSON, `dataSource:"5etools-backfill"`, gap-only/idempotent).
- Data: `73c291d` (the good mm14.json). 872 unit tests green.

## RESULT (live) — MM at 100% monster recall
Baseline 156/450 present, 294 missing. After grounded re-extract: **298 present, 337 grounded, 152 missing**
(recovery pulled 449 sections → 227 recovered candidates → 258→392 total; Aboleth/Mind Flayer/Tarrasque/Owlbear
GROUNDED). After `backfill-monsters`: **present=450, missing=0** (337 grounded + 152 backfilled — dragons +
OCR-lost, e.g. Beholder/Ancient dragons backfilled). mm14.json 230→493 entities (489 Monster). `canonical/validate`
= 0 FAIL-class (86 needsReview soft flags, normal — every book has them). RESIDUAL QUALITY (follow-ups, not
recall): 37 `extra` (canonical monsters not in roster = mis-typed lair-intros/variants/other-source) = precision
tail; 86 mm14 needsReview.

## GOTCHAS caught live
- **Non-root container (uid 1654) can't write host-owned bind-mounted /books** — extract-entities died at the
  100-candidate checkpoint write (`Access to the path .../mm14.progress.json.tmp`). Fix: `docker run --rm -v
  "$(pwd)/books:/books" alpine chown -R 1654:1654 /books`. Real deployment-infra gap. See `mem:operations/running_the_stack`.
- Extraction is SLOW on recovered monsters (~25s→130s/candidate; full section text = long prompts); MM full run ~8h.

## NEXT
DMG + other gated types (MagicItem etc.) = the explicit generalization (same recovery+backfill, all types).
Optional MM cleanup: the 37 extra precision tail. See `mem:project_companion_roadmap`.
