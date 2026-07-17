## ADDED Requirements

### Requirement: A stat-block field-label name SHALL be rejected as a non-entity

`IsEntityLikeName` SHALL reject a candidate name that begins with a stat-block field label — `Armor Class`, `Hit Points`, `Speed`, `Saving Throws`, `Skills`, `Senses`, `Languages`, `Challenge`, `Damage Immunities`, `Damage Resistances`, `Damage Vulnerabilities`, `Condition Immunities`, or a bare ability-score line — regardless of whether the candidate's surrounding text carries a stat-block signature. Such a candidate SHALL be declined before any extraction LLM call rather than emitted as an entity.

#### Scenario: A raw stat line mis-picked as a header is declined
- **WHEN** a candidate name is `"Armor Class 14 (natural armor) Hit Points 71 (13d8 + 13) Speed 30 ft."`
- **THEN** `IsEntityLikeName` returns false and the candidate is declined (not emitted as a Monster entity)

#### Scenario: A field-label fragment is declined
- **WHEN** a candidate name is `"Damage Immunities poison"` (its text sits inside a real stat block so `IsRealEntity` is true)
- **THEN** the candidate is declined by the name gate despite the passing text signature

### Requirement: A sidebar or section heading SHALL be rejected as a non-entity

`IsEntityLikeName` SHALL reject candidate names that are lair headings of the form `"<X> LAIR"` (for any leading token, not only `"A "`-prefixed), optional-rule sidebars beginning `"Effects of "` or `"Variant:"` / `"Variant "`, and similar non-entity section headings. These SHALL be declined before extraction.

#### Scenario: A non-"A"-prefixed lair heading is declined
- **WHEN** a candidate name is `"AN ANARCH's LAIR"`
- **THEN** `IsEntityLikeName` returns false (the lair-heading reject is not limited to the `"A "` prefix)

#### Scenario: Sidebar headings are declined
- **WHEN** a candidate name is `"Effects of the Mold"` or `"Variant: Chromatic Drakes"`
- **THEN** each is rejected as a non-entity heading and declined

### Requirement: Real entity names SHALL still be admitted (no recall regression)

The tightened name gate SHALL continue to admit legitimate entity names. Names that are real creatures, subclasses, races, spells, magic items, or other entities SHALL NOT be rejected by the new patterns; the gate stays conservative (unknown names admit).

#### Scenario: Known-good entity names still admit
- **WHEN** the candidate name is a real entity such as `"Tortle"`, `"Babau"`, `"Archdruid"`, `"Path of the Battlerager"`, or `"Deep Gnome"`
- **THEN** `IsEntityLikeName` returns true and the candidate is admitted to extraction

#### Scenario: A creature name containing a field-label word is not over-rejected
- **WHEN** a candidate name legitimately contains a stat-block word only as an interior token (not a leading field label), e.g. a creature whose name is not itself a field-label line
- **THEN** it is NOT rejected by the stat-block-field-label pattern (the pattern anchors on a leading field label, not a substring match)

### Requirement: The name-gate regression fixture SHALL run without external services

The known-bad and known-good names SHALL be encoded as a unit-test fixture over `IsEntityLikeName` that runs in CI without Docker, Ollama, or a live extraction, so the leak cannot silently return.

#### Scenario: Fixture asserts both directions
- **WHEN** the extraction test suite runs
- **THEN** every documented leaked fragment asserts `IsEntityLikeName == false` and every known-good entity name asserts `IsEntityLikeName == true`
