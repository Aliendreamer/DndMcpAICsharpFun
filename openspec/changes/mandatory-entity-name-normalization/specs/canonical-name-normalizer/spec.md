## MODIFIED Requirements

### Requirement: CanonicalNameNormalizerService normalizes entity names

`CanonicalNameNormalizerService` SHALL process canonical JSON files and for each entity:

- If the name is entirely uppercase (`name.isupper()` equivalent: all letters are uppercase and at least one letter exists and length > 1) AND has no other OCR artifacts (`ExtractionNeedsReview.HasOcrArtifacts` on the lowercased name is false): convert to D&D title case via `EntityNameNormalizer` and set `needsReview = false`.
- If the name has any OCR artifact (as detected by `ExtractionNeedsReview.HasOcrArtifacts`): set `needsReview = true`, leave the name unchanged.
- Otherwise: leave the name unchanged and set `needsReview = false`.

The service SHALL be idempotent: running it twice on the same file produces identical output.

#### Scenario: ALL-CAPS name is title-cased and flag cleared

- **WHEN** an entity has `"name": "CIRCLE OF SPORES"`, no OCR artifacts beyond the all-caps, and `needsReview: true`
- **THEN** the entity name SHALL become `"Circle of Spores"` and `needsReview` SHALL be `false`

#### Scenario: OCR-garbled name is flagged

- **WHEN** an entity has `"name": "Path of the Beast f eature"` (split-word artifact)
- **THEN** the name SHALL remain unchanged and `needsReview` SHALL be `true`

#### Scenario: Clean name is unchanged

- **WHEN** an entity has `"name": "Fighter"` and `needsReview: false`
- **THEN** both fields SHALL remain unchanged

#### Scenario: Idempotency

- **WHEN** the service processes a file twice
- **THEN** the file content SHALL be identical after both runs

### Requirement: D&D title-case algorithm

The title-case conversion (`EntityNameNormalizer`) SHALL:

- Capitalize the first letter of every word except articles and prepositions (`of, the, a, an, in, on, at, to, and, or, but, for, nor`) unless the word starts the name.
- Capitalize the first letter after a hyphen.
- Correct apostrophe-S: `'S` → `'s` (e.g., `TASHA'S` → `Tasha's`).
- Preserve a curated allowlist of D&D acronyms in their canonical casing (`NPC, NPCs, PC, PCs, DM, GP, SP, CP, PP, EP, XP, HP, AC, DC, CR, AoE, D&D`): a word matching the allowlist case-insensitively SHALL be emitted in its allowlist casing rather than lowercased.

#### Scenario: Small word lowercased

- **WHEN** converting `"CIRCLE OF SPORES"`
- **THEN** the result SHALL be `"Circle of Spores"` (not `"Circle Of Spores"`)

#### Scenario: First word is a small word

- **WHEN** converting `"OF MICE AND MEN"`
- **THEN** the result SHALL be `"Of Mice and Men"` (first word capitalized regardless)

#### Scenario: Apostrophe corrected

- **WHEN** converting `"TASHA'S CAULDRON"`
- **THEN** the result SHALL be `"Tasha's Cauldron"` (not `"Tasha'S Cauldron"`)

#### Scenario: Acronym preserved

- **WHEN** converting `"750 GP ART OBJECTS"`
- **THEN** the result SHALL be `"750 GP Art Objects"` (not `"750 Gp Art Objects"`)
