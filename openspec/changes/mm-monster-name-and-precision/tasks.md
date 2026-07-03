## 1. Stat-line name stripping (#1)

- [ ] 1.1 Add `MonsterStatLineName.Strip(string) -> string` (deterministic regex stripper for a trailing `"<Size> <creature-type>[, ...]"` suffix; never returns empty; unchanged when no match). Unit tests: garbled dragon → clean; Animated Armor → clean; Dragon Turtle / Giant Ape / Beholder unchanged; whole-string-is-statline guard.
- [ ] 1.2 Apply `Strip` in `EntityNameMatcher.Match` and `MatchOfType` to `rawName` BEFORE `EntityNameIndex.Normalize`. Unit tests: `Match("ANCIENT BLACK DRAGON Gargantuan dragon, chaotic evil")` returns canonical `Ancient Black Dragon` / Monster; clean names still match as before.

## 2. Extra categorization + precision flag (#2)

- [ ] 2.1 In the monster recall result, split `extra` into `extraOtherSource` (matches a Monster in the full cross-source 5etools index) and `extraUnknown` (matches no 5etools monster). Unit test: Orcus → otherSource, Roa → unknown.
- [ ] 2.2 Admin operation `POST /admin/books/{id}/flag-unknown-monsters` (or fold into the recall/backfill flow) that sets `NeedsReview=true` on each `extraUnknown` monster in the canonical, gap-only, never deletes, never touches `extraOtherSource`. Add to `.http` + insomnia. Unit test: unknown flagged, otherSource untouched, no deletions.

## 3. Verify

- [ ] 3.1 `dotnet build` + `dotnet test` green (sandbox disabled per git-crypt; Docker up).
- [ ] 3.2 Rebuild/redeploy the app; re-extract MM (`extract-entities?force=true`) so #1 takes effect on the canonical. Early spot-check: a dragon (e.g. Ancient Black Dragon) now extracts GROUNDED with the clean name (not the stat-line-garbled form).
- [ ] 3.3 `extract-entities?errorsOnly=true` (#3) to retry the failed candidates → recover more grounded before backfill.
- [ ] 3.4 `GET /admin/books/1/monster-recall`: confirm the dragons are now grounded+present (not extra/backfilled), `extra` dropped substantially, and it reports `extraOtherSource` vs `extraUnknown`.
- [ ] 3.5 `flag-unknown-monsters` → the `extraUnknown` (Lord Soth, Roa, …) are flagged NeedsReview; then `backfill-monsters` for any true residual → recall still 450/450, but with a higher grounded : backfilled ratio and cleaner precision.
- [ ] 3.6 `POST /admin/canonical/validate`; commit the improved canonical + report the before/after (grounded/backfilled/extra) deltas.
