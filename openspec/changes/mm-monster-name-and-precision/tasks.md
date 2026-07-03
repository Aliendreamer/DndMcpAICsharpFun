## 1. Stat-line name stripping (#1)

- [x] 1.1 Add `MonsterStatLineName.Strip(string) -> string` (deterministic regex stripper for a trailing `"<Size> <creature-type>[, ...]"` suffix; never returns empty; unchanged when no match). Unit tests: garbled dragon → clean; Animated Armor → clean; Dragon Turtle / Giant Ape / Beholder unchanged; whole-string-is-statline guard.
- [x] 1.2 Apply `Strip` in `EntityNameMatcher.Match` and `MatchOfType` to `rawName` BEFORE `EntityNameIndex.Normalize`. Unit tests: `Match("ANCIENT BLACK DRAGON Gargantuan dragon, chaotic evil")` returns canonical `Ancient Black Dragon` / Monster; clean names still match as before.

## 2. Extra categorization + precision flag (#2)

- [x] 2.1 In the monster recall result, split `extra` into `extraOtherSource` (matches a Monster in the full cross-source 5etools index) and `extraUnknown` (matches no 5etools monster). Unit test: Orcus → otherSource, Roa → unknown.
- [x] 2.2 Admin operation `POST /admin/books/{id}/flag-unknown-monsters` (or fold into the recall/backfill flow) that sets `NeedsReview=true` on each `extraUnknown` monster in the canonical, gap-only, never deletes, never touches `extraOtherSource`. Add to `.http` + insomnia. Unit test: unknown flagged, otherSource untouched, no deletions.

## 3. Verify

- [x] 3.1 `dotnet build` + `dotnet test` green (sandbox disabled per git-crypt; Docker up).
- [x] 3.2 SUPERSEDED — the 8h `force` re-extract was replaced by the in-place `mm-canonical-name-cleanup` console (reuses the same matcher + id logic, so the result ≡ a re-extract). Realized on `mm14.json` at commit e5d4d59.
- [x] 3.3 `extract-entities?errorsOnly=true` (#3) ran — recovered ~9 of 11 failed candidates.
- [x] 3.4 SATISFIED by `mm-canonical-name-cleanup`: `monster-recall` reports 450/450, extraOtherSource vs extraUnknown split observed; dragons grounded+present with clean names.
- [x] 3.5 SATISFIED by `mm-canonical-name-cleanup`: `flag-unknown-monsters` flagged the extraUnknown (Roa, DRAGON TURTLE, …) NeedsReview; recall still 450/450 with grounded:backfilled 337:136 (was 337:152).
- [x] 3.6 SATISFIED by `mm-canonical-name-cleanup` (commit e5d4d59): `canonical/validate` → 0 failures for mm14; improved canonical committed with before/after deltas reported.
