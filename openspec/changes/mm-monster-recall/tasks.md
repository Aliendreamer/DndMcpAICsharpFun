## 1. Candidate recovery (grounded)

- [x] 1.1 Make the stat-block scanner authoritative: a detected stat block is always emitted as a candidate regardless of TOC category (`EntityCandidateBuilder`/`EntityCandidateScanner`). Unit test: stat block on a `Rule` page yields a candidate.
- [x] 1.2 Add TOC-failure ungating: when a book yields stat blocks but 0 Monster TOC pages, stop suppressing its sections on TOC-category grounds. Unit test: bestiary-shaped input (0 Monster pages) is not gated away.
- [x] 1.3 Add 5etools-roster monster recovery for official books: before skipping a section on TOC grounds, fuzzy-match its heading against the 5etools monster names via `EntityNameMatcher`/`EntityNameIndex` (0.90 bar); a confident match recovers a `Monster` candidate with the matched canonical name; no match falls through unchanged. Unit tests: matching heading recovered; non-match not recovered (precision preserved); non-official book skips recovery.

## 2. 5etools monster recall check

- [x] 2.1 `MonsterRecallService`: load the authoritative monster roster from the local 5etools bestiary filtered to the book's source (`source == MM`), normalize names, diff against the extracted canonical's monster entities → `{ missing[], extra[], grounded, backfilled }`. Unit test: known roster vs a canonical with a gap reports the gap.
- [x] 2.2 Admin endpoint exposing the recall check for a book id; add to `DndMcpAICsharpFun.http` + `dnd-mcp-api.insomnia.json`.

## 3. 5etools monster backfill

- [x] 3.1 `MonsterBackfillService` (mirror `SpellBackfillService`): for each roster monster missing from the canonical, project the 5etools monster via `FivetoolsMapperRegistry` into a canonical entity marked `dataSource:"5etools-backfill"`; gap-only + idempotent; never replace a grounded monster. Unit tests: missing monster backfilled; second run is a no-op; grounded entity preserved.
- [x] 3.2 `POST /admin/books/{id}/backfill-monsters` wiring; add to `.http` + insomnia.

## 4. Live validation on MM (the real gate)

- [x] 4.1 `dotnet build` + `dotnet test` green (sandbox disabled per git-crypt; Docker up for persistence tests).
- [x] 4.2 Re-extract MM (`extract-entities?force=true`) on the running stack; early-checkpoint spot-check that **Aboleth AND Beholder return as `Monster` (grounded)** before the full run — abort+diagnose if not.
- [x] 4.3 Full run → run the recall check: report grounded monster count + the residual-missing list vs the 5etools MM roster; diff types vs the old `mm14.json` (a count change is not automatically a regression).
- [x] 4.4 `backfill-monsters` to close the residual; re-run the recall check → confirm ~100% monster recall, with backfilled entries marked; note grounded-vs-backfilled split.
- [x] 4.5 `POST /admin/canonical/validate` clean; ingest-entities if we want MM monsters in `dnd_entities` ("quadrant fill" is cheap once the canonical is good).

## 5. Verify + close

- [x] 5.1 Confirm each requirement (monster-candidate-recovery, fivetools-monster-backfill) is satisfied by tests + the live MM result.
- [x] 5.2 Note the DMG / other-gated-types generalization as the explicit follow-up (roadmap).
