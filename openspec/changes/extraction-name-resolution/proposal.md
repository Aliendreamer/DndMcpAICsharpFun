## Why

The resolver re-runs (MM/PHB/DMG) silently dropped ~50 real entities — `Fireball`, `Aboleth`,
`Bard`, `Lion`, `Counterspell`, `Wish` — confirmed root cause: `ExtractionSignatures.IsEntityLikeName`'s
all-caps single-word rule (`n.Length >= 4 && n == n.ToUpperInvariant() && !n.Contains(' ')`). Marker
renders entity names as ALL-CAPS headings, so a rule meant for stat-block sub-headers (`ACTIONS`,
`REACTIONS`) also nukes every single-word entity; multi-word names (`MAGIC MISSILE`) escape via the
space. The prior opus corpus-validation missed it because it tested Title-Case *entity* names, not
the raw all-caps *candidate* names. This is a data-integrity emergency: a dropped Fireball poisons
everything downstream, and the resolver canonical is **cleaner but incomplete** — not safe to ingest.

We have the full 106 MB 5etools mirror locally (`5etools/`), with verified-complete coverage of the
official corpus (361 PHB spells, 3733 monsters across all sources, every class/background). That is
the ground truth that fixes both recall *and* name-quality at once.

## What Changes

- **Add `EntityNameIndex` + `EntityNameMatcher`** — match each candidate's raw heading against the
  full 5etools name corpus. A match (above a fuzzy-confidence threshold) yields the **clean canonical
  name** and the **deterministic entity type** (`Fireball`→Spell, `Lion`→Monster, `Bag of Holding`→
  MagicItem). Cross-source monsters are matched against the WHOLE corpus (PHB animals are catalogued
  under MM). Index TOP-LEVEL types only (spell/monster/item/class/background/race/feat/condition/
  deity→God/plane→Plane); **NOT** optionalfeatures/subclass-features (those are the feature-noise).
- **Extend `DeterministicTypeResolver`** — a 5etools match becomes ladder step 1 (highest priority):
  force the matched type, use the canonical name as the entity Name/id, skip the LLM type-decision.
- **Fix `ExtractionSignatures.IsEntityLikeName`** — replace the broad all-caps rule with a
  structural-sub-header **denylist** (`ACTIONS/REACTIONS/TRAITS/BONUS ACTIONS/LEGENDARY ACTIONS/
  LAIR ACTIONS/REGIONAL EFFECTS`) and add a **lair-name reject** (`A …'s LAIR`).
- **Safety:** matching never DROPS — no match falls through to the content-first union (declines junk,
  never wrongly drops a real-but-unlisted entity). Homebrew/non-5etools books use content-first
  throughout. Field extraction stays prose-grounded — 5etools sets name+type only, never content.
- **Validate:** re-run all 4 books; recall recovers, precision holds, names come out clean.

## Capabilities

### New Capabilities
- `entity-name-resolution`: the 5etools-backed name index + fuzzy matcher that resolves a raw
  candidate heading to a canonical entity name + type, used as a deterministic keep/clean/type signal.

### Modified Capabilities
- `deterministic-type-resolution`: the resolver ladder gains a first-priority 5etools-match step;
  the entity-like-name quality check replaces the all-caps rule with a structural-header denylist +
  lair-name reject.

## Impact

- New: `Features/Ingestion/EntityExtraction/EntityNameIndex.cs`, `EntityNameMatcher.cs` (+ a
  5etools→`EntityType` mapping); DI registration; the index loads from `5etools/*.json` at startup.
- Modified: `DeterministicTypeResolver` (5etools step + canonical-name carry), `ExtractionSignatures`
  (`IsEntityLikeName` rule swap), `EntityExtractionOrchestrator` (use the canonical name for matched).
- Validation: re-run MM/PHB/DMG/(4th) on the new pipeline.
- Out of scope (deferred): standalone OCR de-spacing for unmatched residual; 5etools for content/fields.
