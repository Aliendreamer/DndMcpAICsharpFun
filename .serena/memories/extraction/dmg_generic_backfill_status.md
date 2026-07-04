# dmg-generic-backfill — DONE + ARCHIVED (2026-07-04)

Change `dmg-generic-backfill` is **complete, merged, validated on real DMG data, and ARCHIVED**
(`openspec/changes/archive/2026-07-04-dmg-generic-backfill/`). Spec sync: new capability
`fivetools-entity-backfill` (8 reqs) supersedes+retires `fivetools-monster-backfill`,
`fivetools-spell-backfill`, `monster-precision-flagging`.

## What shipped (code, earlier commits) — the generic engine
EntityBackfillService + IFivetoolsBackfillProvider + 4 providers (Monster/Spell/MagicItem/God) +
FivetoolsEntryText.Flatten (rich recursive entry flattening) + MagicVariantExpander (+N variants).
Type-parameterized routes REPLACED the old per-type ones:
- GET  /admin/books/{id}/entity-recall?type=Monster|Spell|MagicItem|God
- POST /admin/books/{id}/backfill-entities?type=...
- POST /admin/books/{id}/flag-unknown-entities?type=...

## DMG DATA RUN — DONE (book id 3, key DMG)
Full flow executed end-to-end and committed (`e8ea8d2`):
- Fresh content-first re-extract (qwen3, ~3.35h): 426 clean / 5 err / 92 declined. 14 bogus Class GONE.
- errorsOnly retry recovered 4/5 (Flame Tongue, Robe of Eyes, Scarab of Protection, d12 Quirk);
  Ioun Stone re-failed but recovered via 5etools backfill.
- Deterministic backfill/flag/validate: 551→1321 entities (MagicItem 1069 incl variants, God 21
  Dawn War pantheon, Monster+2), ALL 1321 ids unique, canonical/validate 0 failures, 247 NeedsReview.
- ingest-entities: 1321 entities reprojected into Qdrant dnd_entities (Ollama up).

## KNOWN RESIDUAL → new spec
Siege weapons (Ballista/Cannon/Ram/Cauldron) persist as EMPTY Item shells with the model's
classification reasoning leaked into canonicalText — schema has no Object type for AC/HP-bearing
non-creatures. Spec'd + committed (`9ad8f3b`): `openspec/changes/object-entity-type-and-decline-leak`
(new Object type + decline-not-leak fix). NOT yet implemented at time of writing.

## Infra state (2026-07-04)
GPU works (RTX 5070, qwen3 100% GPU) — earlier "GPU down" was a sandbox false-negative
(`mem:project/sandbox_blocks_gpu`-equiv; always dangerouslyDisableSandbox for nvidia-smi).
App container rebuilt on fresh image (new routes live). books/ chmod a+rwX so the container
(uid 1654) can write canonical files. Perf sweep (Frozen + [GeneratedRegex]) committed `df26884`
but only takes effect on next image rebuild. Relates to `mem:companion_roadmap`.

## openspec archive gotcha (learned)
`openspec archive` ABORTS when a REMOVED-requirements delta would empty a spec ("Spec must have
at least one requirement"). To retire a capability fully: delete its delta file from the change +
`git rm` its main spec, THEN archive. Also this CLI version writes the ADDED spec file even on an
aborted run, so re-runs hit "already exists" — clean up before retry.
