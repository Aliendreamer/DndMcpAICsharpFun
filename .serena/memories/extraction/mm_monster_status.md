# WHERE WE ARE — Monster Manual entity recall/quality (2026-07-03)

Goal: core 2014 books (PHB/MM/DMG) to PHB-quality entity recall, then 2024. **MM is essentially DONE; DMG
is next.** Two shipped openspec changes (both NOT archived yet):

## 1. `mm-monster-recall` — DONE + committed, live-validated
Fixed the upstream candidate-generation gap (MM classified 100% TOC `Rule` → 449 monster sections skipped).
Recovery (`EntityCandidateScanner` fuzzy-matches skipped headings vs 5etools Monster roster) + 5etools
backfill. **Result: MM 156/450 → 450/450 monster recall, 0 missing** (337 grounded + 152 backfilled).
Good canonical committed at **73c291d** (`books/canonical/mm14.json`, 493 entities). 872 unit tests. Also
caught+fixed a non-root bind-mount write bug (see `mem:operations/running_the_stack`).

## 2. `mm-monster-name-and-precision` — CODE DONE + committed + VERIFIED; DATA-cleanup NOT applied
Three improvements from the result deep-dive (commits a89c372 #1, a2f7a8a #2, 38e7dff chore; 891 tests):
- **#1 stat-line name strip** (`MonsterStatLineName.Strip` + applied in `EntityNameMatcher.Scan`): garbled
  names like `ANCIENT BLACK DRAGON Gargantuan dragon, chaotic evil` (a REAL grounded dragon, hp=367, just
  misnamed) now resolve to the clean 5etools `Ancient Black Dragon`. VERIFIED by unit test (definitive for a
  deterministic strip). This stops the ~37 dragons being both `extra` (garbled) AND `backfilled` (clean) =
  duplicates.
- **#2 extra split + flag**: `monster-recall` now splits `extra` into `extraOtherSource` (in 5etools under
  another source, e.g. Orcus/MPMM, Lord Soth/Dragonlance — plausibly real cross-printing) vs `extraUnknown`
  (in no bestiary — false positives like Roa). `POST /admin/books/{id}/flag-unknown-monsters` sets
  NeedsReview on `extraUnknown` (never deletes, never touches otherSource). VERIFIED unit + LIVE (split
  observed: 12 otherSource / 25 unknown on the current canonical).
- **#3 errorsOnly retry**: VERIFIED — ran, recovered ~9 of the 11 failed candidates.

## ⚠️ PENDING (the data-cleanup, NOT done — user aborted the ~8h run 2026-07-03)
The #1 fix is CODE-only so far. The 37 garbled dragons + their duplicate backfills are STILL in the
committed `mm14.json` (493). Applying #1 to the data needs a full `force` re-extract (~8h) OR a cheaper
in-place canonical name-fix (run the matcher over the existing garbled monster names + de-dupe backfills —
NOT yet built/investigated; was offered as "abort + cheaper cleanup"). Also NOT run: `flag-unknown-monsters`
(must run AFTER the dragon names are cleaned, else it wrongly flags the garbled-but-real dragons).
`mm-monster-name-and-precision` tasks 3.2/3.4/3.5/3.6 (live re-extract + flag + backfill + commit improved
canonical) are OPEN.

## State of the machine
Working tree CLEAN (canonical reverted to committed 493). `books/` chowned back to host uid — **re-chown to
1654 before the container extracts again** (`docker run --rm -v "$(pwd)/books:/books" alpine chown -R
1654:1654 /books`). App container running. User stops the PC ~2h after 2026-07-03 ~08:00.

## NEXT SESSION options
(a) cheaper in-place dragon-name cleanup (investigate: rewrite garbled monster names via the matcher, dedupe
backfills) — avoids 8h; (b) full `force` re-extract to realize #1 (8h); then flag-unknown + backfill +
validate + commit improved canonical + report grounded:backfilled delta (was 337:152); (c) DMG — generalize
recovery+backfill to all gated types (MagicItem-heavy). Both changes still need archiving after their
data steps. See `mem:project_companion_roadmap`, `mem:operations/running_the_stack`.
