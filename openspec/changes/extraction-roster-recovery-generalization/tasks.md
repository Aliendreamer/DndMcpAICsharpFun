## 1. Index carries source, and indexes subraces (one atomic task — the tuple retype does not compile half-done)

- [ ] 1.1 In `EntityNameIndex`, widen the entry tuple from `(string Canonical, EntityType Type)` to include `string Source`, updating `Entries`, `EntriesByName`, `Add` and every `LoadGlob` call. Read the 5etools `source` property per element; use an empty string when absent. Behaviour-preserving otherwise: same first-wins, same one-entry-per-distinct-type multimap, same load order.
- [ ] 1.2 Update `EntityNameMatcher.Scan` and every other consumer of the tuple so the solution builds 0/0 in this same task (`find_referencing_symbols` on `EntriesByName`/`Entries` first — a caller left behind leaves a red commit).
- [ ] 1.3 Load the `subrace` array from `races.json` as `EntityType.Subrace`, **after** the base-race load so first-wins resolves a colliding name to `Race`.
- [ ] 1.4 Tests: an entry exposes its source; `"Draconblood"` and `"Pallid"` match as `Subrace`; a race/subrace name collision resolves to `Race`; and existing `Match`/`MatchOfType` results are unchanged (pin at least one previously-asserted lookup).

## 2. Source-preferred matching

- [ ] 2.1 Add a source-preferred lookup to `EntityNameMatcher` (same normalisation, same ±2 fuzzy bound, same 0.90 ratio, same ordinal tie-break): prefer entries whose `Source` equals the supplied key, else fall back to the existing global first-wins result. Do NOT alter `Match` or `MatchOfType`.
- [ ] 2.2 Tests: same-source entry preferred (`EGW` + `"Orc"` → the EGW `Race` entry, not a bestiary `Monster`); global fallback when the book has no such entry; null when nothing matches at the bar; and a regression test asserting `Match`/`MatchOfType` are byte-identical to before.

## 3. Generalized recovery in the scanner

- [ ] 3.1 In `EntityCandidateScanner.Scan`, replace the `MatchOfType(s.Section, EntityType.Monster)` recovery with the source-preferred lookup over all types. Emit the candidate with **the matched entry's type** as the primary, and its prior from that type (not `ContentCategory.Monster`). Keep the existing log line but report the recovered type and source.
- [ ] 3.2 Rename the `recoverMonsters` parameter to `recoverFromRoster` and thread the book's source key through from `EntityCandidateBuilder` (which already computes `record.FivetoolsSourceKey is not null`). Update all call sites and tests in this same task.
- [ ] 3.3 Confirm the `ungateOnTocFailure` path for keyless books is untouched and still mutually exclusive with recovery.
- [ ] 3.4 Tests: a `Rule`-page heading matching a subclass yields a `Subclass` candidate; a heading matching a race yields `Race`; a non-matching heading yields no candidate (precision unchanged); a keyless book runs no recovery. Each must FAIL against the Monster-only implementation — a test that also passes before gates nothing.

## 4. Gates

- [ ] 4.1 `dotnet build` 0/0; FULL `dotnet test --filter "FullyQualifiedName!~Persistence"` green (not a feature-filtered subset — the tuple retype is cross-cutting).
- [ ] 4.2 `dotnet format DndMcpAICsharpFun.slnx --include <changed files> --verify-no-changes` clean for changed files only.

## 5. Live validation (the only proof recovery actually fires)

- [ ] 5.1 Rebuild the app image (`docker compose -f docker-compose.yml up -d --build app`, no key env vars).
- [ ] 5.2 Force re-extract EGW (`POST /admin/books/16/extract-entities?force=true`). Confirm from the logs that the candidate count rose well above 101 and that `Skipping section` warnings for the pp. 162–190 chapter are replaced by recovery lines naming the recovered type.
- [ ] 5.3 Per-entity diff against the 82-entity baseline: confirm Echo Knight / Chronurgy Magic / Graviturgy Magic appear as `Subclass`; the four subraces as `Subrace`; the dunamancy spells as `Spell`; and that the five known mis-typed names (`Sea Elf`, `Aarakocra`, `Kenku`, `Orc`, `Tortle`) moved OFF `Monster` to their correct types rather than being duplicated.
- [ ] 5.4 Read `egw.declined.json` for anything wrongly declined by the widened matching, and confirm no previously-good entity was lost.
- [ ] 5.5 Re-extract SCC (17) and GGR (18) and record their before/after type histograms.
- [ ] 5.6 Record the evidence (candidate counts, per-entity verdicts, decline audit) in the change folder — a count rise is not self-evidently good.
