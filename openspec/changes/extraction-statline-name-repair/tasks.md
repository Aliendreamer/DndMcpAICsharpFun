## 1. Strip helper

- [ ] 1.1 Add `Features/Ingestion/EntityExtraction/MagicItemStatLineName.cs`: a `static partial class` with a `[GeneratedRegex]` matching a **trailing** magic-item stat line (leading `\s+`, then the `<category-token> … , <rarity>` shape from `ExtractionSignatures.MagicItemHeader()`, to end of string) and `Strip(string rawName)` returning the input unchanged when no match OR when stripping would leave an empty string. Mirror `MonsterStatLineName` exactly in shape and contract.
- [ ] 1.2 Unit-test `Strip` in both directions: `"AMULET OF THE DRUNKARD Wondrous item, uncommon"` → `"AMULET OF THE DRUNKARD"`; `"Wondrous item, uncommon"` → unchanged (whole input is the stat line); `"Moorbounder"` → unchanged; `"Weapon (greataxe), very rare (requires attunement)"` → unchanged (whole input); `"Sun Blade Weapon (longsword), rare (requires attunement)"` → `"Sun Blade"`.

## 2. Name gate

- [ ] 2.1 Extend `ExtractionSignatures.IsEntityLikeName` to reject a name that is **entirely** a magic-item stat line, defined via `MagicItemStatLineName` so the reject and the repair share one definition of the shape (do NOT add a second independent regex).
- [ ] 2.2 Add the regression fixture required by the spec: every stat-line name observed in the live EGW/SCC output asserts `IsEntityLikeName == false` (`"Wondrous item, very rare"`, `"Potion, very rare"`, `"Wondrous Item, Uncommon (Requires Attunement by a Spellcaster)"`, `"Weapon (greataxe), very rare (requires attunement)"`, `"Armor (studded leather), legendary (requires attunement)"`, `"Wondrous item, common"`, `"Wondrous item, uncommon"`, `"Wondrous item, legendary"`, `"Potion, rare"`, `"Ring, rare (requires attunement)"`, `"Weapon (whip), artifact (requires attunement)"`, `"Weapon (mace), artifact (requires attunement)"`), and every known-good name asserts `true` (`"Ring of Spell Storing"`, `"Acheron Blade"`, `"Battering Shield"`, `"Brooch of Living Essence"`, `"Moorbounder"`, `"Path of the Battlerager"`). Confirm each bad-name assertion FAILS on the pre-change gate — a test that passes both before and after gates nothing.

## 3. Matcher and candidate name (one atomic task — see design "Order is load-bearing")

- [ ] 3.1 In `EntityNameMatcher.Scan`, apply `MagicItemStatLineName.Strip` in addition to `MonsterStatLineName.Strip` before `EntityNameIndex.Normalize`, so a concatenated heading grounds to its real 5etools entry.
- [ ] 3.2 In `EntityCandidateBuilder`, repair the candidate `DisplayName` with `MagicItemStatLineName.Strip` **before** the `DeterministicTypeResolver.Resolve` drop-prefilter runs, so the emitted entity name is clean and a repaired name is matched (and therefore never dropped) rather than gated on its raw form.
- [ ] 3.3 Add a test proving the ordering constraint: a candidate named `"AMULET OF THE DRUNKARD Wondrous item, uncommon"` whose stripped name matches a 5etools entry is NOT dropped and carries the clean `DisplayName`; and a candidate named only `"Wondrous item, uncommon"` IS dropped.

## 4. Gates

- [ ] 4.1 `dotnet build` 0 warnings / 0 errors; `dotnet test --filter "FullyQualifiedName!~Persistence"` fully green (not a feature-filtered subset — a shared-signature change can move tests in unrelated files).
- [ ] 4.2 `dotnet format DndMcpAICsharpFun.slnx --include <changed files> --verify-no-changes` clean for the changed files only (the repo carries pre-existing violations elsewhere; do not fix those).

## 5. Live validation (unit-green is NOT sufficient)

- [ ] 5.1 Rebuild the app image (`docker compose -f docker-compose.yml up -d --build app`, no key env vars) — the image does not hot-reload, so a stale container silently tests the old code.
- [ ] 5.2 Force re-extract EGW (`POST /admin/books/16/extract-entities?force=true`) — the book with the densest magic-item chapter and 19 known junk entities.
- [ ] 5.3 Diff the new `books/canonical/egw.json` against the captured 82-entity baseline **per entity**, not by count: confirm every removed entity is a stat-line name, confirm no real magic item (`Acheron Blade`, `Battering Shield`, `Brooch of Living Essence`, `Butcher's Bib`, `Coin of Delving`, `Corpse Slayer`, `Duskcrusher`, `Ersatz Eye`, …) disappeared, and confirm any concatenated name now appears under its clean/canonical form. Inspect `egw.declined.json` for anything wrongly declined.
- [ ] 5.4 Record the before/after type histogram and the per-entity verdict in the change folder so the "a count drop is a correction, not a regression" judgement is evidenced rather than asserted.
