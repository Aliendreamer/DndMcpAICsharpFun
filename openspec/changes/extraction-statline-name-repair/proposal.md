## Why

Magic-item stat lines are being used as entity **names**, so extraction emits junk canonical entities. The live Explorer's Guide to Wildemount run (`books/canonical/egw.json`, 82 entities) produced 7 `Item` entities and most of its 12 `Rule` entities named things like `"Wondrous item, very rare"`, `"Weapon (greataxe), very rare (requires attunement)"`, `"Potion, very rare"`, `"Armor (studded leather), legendary (requires attunement)"`, and `"AMULET OF THE DRUNKARD Wondrous item, uncommon"`. Strixhaven shows the same shape (`"Wondrous Item, Uncommon (Requires Attunement by a Spellcaster)"`). That is roughly 19 of EGW's 82 entities — about 23% of the book's output is unusable, and it will pollute `dnd_entities` on ingest.

The root cause is a one-line gap: `ExtractionSignatures.IsEntityLikeName` has no rule for a magic-item stat line used as a name, so `DeterministicTypeResolver` does not `Drop` the candidate, and `IsMagicItem(text)` then forces it to `MagicItem` (or the `Item`/`Rule` rescue). The shape is *already* recognised elsewhere in the same file by the `MagicItemHeader()` regex — the gate simply never consults it. Fixing this now matters because three setting books (EGW, SCC, GGR) are about to be gap-filled and ingested, and a re-extract is already planned.

## What Changes

- Add `MagicItemStatLineName.Strip(rawName)`, a sibling of the existing `MonsterStatLineName.Strip`: it removes a **trailing** magic-item stat line and returns the input unchanged when stripping would leave nothing. This repairs the concatenated form so `"AMULET OF THE DRUNKARD Wondrous item, uncommon"` becomes `"AMULET OF THE DRUNKARD"`.
- `EntityNameMatcher` strips magic-item stat lines as well as monster ones before index lookup, so a concatenated name resolves to its real 5etools entry instead of missing entirely.
- `IsEntityLikeName` rejects a name that is **entirely** a magic-item stat line (nothing meaningful remains after stripping), so such candidates are dropped before any LLM call.
- Repair the candidate `DisplayName` at candidate-build time so the emitted entity carries the clean name, not just the matcher's internal view of it.

Not in scope: other junk-name shapes visible in the same output (`"Magic Item Descriptions"` typed `MagicItem`, `"Spell Level Spells"` typed `Lore`). Those are generic section headings with a different root cause and no single regex covers them; chasing them here would turn a precise fix into keyword whack-a-mole.

## Capabilities

### New Capabilities

None. This closes a gap in existing behaviour.

### Modified Capabilities

- `entity-name-noise-gate`: adds a requirement that a name which is entirely a magic-item stat line is rejected as a non-entity, and that a name with a trailing stat line is repaired rather than rejected.
- `entity-name-resolution`: candidate-name matching strips a trailing magic-item stat line before index lookup, in addition to the existing monster stat-line strip.

## Impact

- `Features/Ingestion/EntityExtraction/MagicItemStatLineName.cs` (new)
- `Features/Ingestion/EntityExtraction/ExtractionSignatures.cs` — `IsEntityLikeName`
- `Features/Ingestion/EntityExtraction/EntityNameMatcher.cs` — `Scan`
- `Features/Ingestion/EntityExtraction/EntityCandidateBuilder.cs` — `DisplayName` repair
- No endpoint, schema, or persistence change; no migration.

**Ordering constraint:** `DeterministicTypeResolver` never drops a name that matches a 5etools entry, so the strip must run **before** the match. Getting that order wrong would drop real magic items instead of repairing them.

**Validation constraint:** unit-green is not sufficient. This requires a live re-extract and a diff of *which* entities changed. The entity count is expected to **fall** (junk removed, some names merged onto their real 5etools entry) — a drop here is a correction, not a regression, and must be confirmed per entity rather than assumed either way.
