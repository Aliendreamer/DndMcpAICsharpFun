## 1. 5etools entity-name index

- [x] 1.1 Add a `FivetoolsType → EntityType` mapping (spell→Spell, monster→Monster, item→Item/MagicItem-by-rarity, class→Class, background→Background, race→Race, feat→Feat, condition→Condition, deity→God) (TDD: the rarity→MagicItem branch)
- [x] 1.2 `EntityNameIndex` (singleton): load `5etools/*.json` for the TOP-LEVEL types only, build `normalizedName → (canonicalName, EntityType)`; monsters from the FULL bestiary (all sources); EXCLUDE optionalfeatures/subclass-features (TDD: Fireball→Spell, Lion→Monster present; Spellcasting/Archery absent)
- [x] 1.3 Verify the index covers the known losses (Fireball, Aboleth, Bard, Lion, Counterspell, Mage Armor) and is built once

## 2. EntityNameMatcher (exact + fuzzy)

- [x] 2.1 Normalize a raw heading (case/punctuation/whitespace) for lookup keys (TDD)
- [x] 2.2 `EntityNameMatcher.Match(raw)` → exact normalized index hit (TDD: "FIREBALL"→("Fireball",Spell))
- [x] 2.3 Fuzzy fallback with a confidence threshold (TDD: "MAGEARMOR"→("Mage Armor",Spell) accepted; "ACTIONS"/"A RED DRAGON'S LAIR" → null; a below-threshold heading → null, never a wrong neighbour)

## 3. Fix IsEntityLikeName (denylist + lair, drop the all-caps rule)

- [x] 3.1 In `ExtractionSignatures`, replace the all-caps single-word rule with a structural-sub-header denylist (ACTIONS/REACTIONS/TRAITS/BONUS ACTIONS/LEGENDARY ACTIONS/LAIR ACTIONS/REGIONAL EFFECTS) + a lair-name reject (`^A .*'?s LAIR$`) (TDD: FIREBALL/ABOLETH/BARD/LION now TRUE; ACTIONS/REACTIONS/"A RED DRAGON'S LAIR" FALSE; existing Step/Challenge/Appendix/Creating/Features still FALSE)
- [x] 3.2 Run the existing `ExtractionSignaturesTests` + `DeterministicTypeResolverTests` — reconcile any that asserted the old all-caps behaviour to the new rules (intended change)

## 4. Extend DeterministicTypeResolver (5etools step 1)

- [x] 4.1 Add `EntityNameMatcher` to `DeterministicTypeResolver`; make a 5etools match ladder STEP 1 (before drop): return a forced type + the canonical name (TDD: "FIREBALL"→Force(Spell)+"Fireball"; all-caps match is NOT dropped; unmatched falls to the existing drop/Monster/MagicItem/Defer ladder)
- [x] 4.2 Extend `TypeResolution` (or the return shape) to carry the resolved canonical name when matched

## 5. Wire canonical name + forced type into the orchestrator

- [x] 5.1 In `EntityExtractionOrchestrator.ExtractOneAsync`/candidate build, when the resolver returns a 5etools match: use the **canonical name** for the entity `Name` + `EntityIdSlug`, and extract with the matched type's schema (forced path) — skip the union (TDD via the orchestrator harness: a matched candidate → entity with canonical name + forced type)
- [x] 5.2 Register `EntityNameIndex`/`EntityNameMatcher` in DI (singleton); ensure the index loads from `5etools/` (config path)
- [x] 5.3 `dotnet build` 0 warnings; full non-persistence suite green (reconcile intended-change tests)

## 6. Live validation — re-run all 4 books

- [x] 6.1 Rebuild the app image; recreate the app; ensure qwen3 on GPU; confirm 5etools index loads
- [x] 6.2 Re-extract MM, PHB, DMG, and the 4th book (force) on the new pipeline
- [x] 6.3 Validate vs the recall losses: Fireball→Spell, Aboleth→Monster, Bard→Class, Lion→Monster, Counterspell→Spell all present + correctly typed; names clean (Mage Armor not MAGEARMOR); precision holds (no ACTIONS/REACTIONS/lair Monsters); compare accepted spell/class counts to the prior playerhandbook-2014 run (recall recovered)
- [x] 6.4 Record before/after deltas in the change; then ready to archive + (separately) ingest the corrected canonical
