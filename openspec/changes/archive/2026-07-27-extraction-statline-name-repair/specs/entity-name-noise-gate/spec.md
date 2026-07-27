## ADDED Requirements

### Requirement: A magic-item stat line used as a name SHALL be rejected as a non-entity

`IsEntityLikeName` SHALL reject a candidate name that consists **entirely** of a magic-item stat line — a category token (`weapon`, `armor`/`armour`, `ring`, `rod`, `staff`, `wand`, `potion`, `scroll`, `wondrous item`), optionally with a parenthesised sub-type and/or attunement clause, followed by a rarity (`common`, `uncommon`, `rare`, `very rare`, `legendary`, `artifact`). Such a candidate SHALL be dropped before any extraction LLM call, regardless of whether its surrounding text carries a magic-item signature.

This closes the gap by which `DeterministicTypeResolver` admitted the candidate (the name gate passed it), and `IsMagicItem(text)` then forced it to `MagicItem`, or the `Item`/`Rule` rescue captured it — emitting an entity whose name is a stat line.

#### Scenario: A bare magic-item stat line is dropped

- **WHEN** a candidate name is `"Wondrous item, very rare"`, `"Potion, very rare"`, or `"Wondrous Item, Uncommon (Requires Attunement by a Spellcaster)"`
- **THEN** `IsEntityLikeName` returns false and the candidate is dropped before extraction (no entity is emitted)

#### Scenario: A stat line with a parenthesised sub-type and attunement clause is dropped

- **WHEN** a candidate name is `"Weapon (greataxe), very rare (requires attunement)"` or `"Armor (studded leather), legendary (requires attunement)"`
- **THEN** `IsEntityLikeName` returns false and the candidate is dropped

#### Scenario: A rarity word alone does not trigger the reject

- **WHEN** a candidate name legitimately contains a rarity or category word without forming a stat line, e.g. `"Ring of Spell Storing"` or `"Rare Book of Shadows"`
- **THEN** `IsEntityLikeName` returns true (the pattern requires the `<category>, <rarity>` stat-line shape, not a mere keyword)

### Requirement: A name with a TRAILING magic-item stat line SHALL be repaired, not rejected

When a candidate name is a real entity name with a magic-item stat line appended — the shape MinerU produces when an item heading and its stat line collapse into one heading — the trailing stat line SHALL be stripped and the remaining name used. The candidate SHALL NOT be dropped, because a real entity is present.

Stripping SHALL return the input unchanged when it would leave nothing, so that the "entirely a stat line" case falls through to the rejection requirement above rather than producing an empty name.

#### Scenario: A concatenated item heading is repaired

- **WHEN** a candidate name is `"AMULET OF THE DRUNKARD Wondrous item, uncommon"`
- **THEN** the name is repaired to `"AMULET OF THE DRUNKARD"` and the candidate is admitted to extraction with that clean name

#### Scenario: Stripping that would empty the name leaves it unchanged

- **WHEN** the whole input is a stat line, e.g. `"Wondrous item, uncommon"`
- **THEN** the strip returns the input unchanged (and the name gate rejects it)

#### Scenario: A name with no stat line is untouched

- **WHEN** a candidate name is `"Moorbounder"` or `"Path of the Battlerager"`
- **THEN** the strip returns it unchanged and the gate admits it

### Requirement: The magic-item name fixture SHALL run without external services

The known-bad stat-line names and known-good entity names SHALL be encoded as a unit-test fixture over `IsEntityLikeName` and the strip helper, running in CI without Docker, Ollama, or a live extraction, so this leak cannot silently return.

#### Scenario: Fixture asserts both directions

- **WHEN** the extraction test suite runs
- **THEN** every stat-line name observed in the EGW and SCC output asserts `IsEntityLikeName == false`, and every known-good entity name asserts `IsEntityLikeName == true`
