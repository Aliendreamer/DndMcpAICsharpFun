# canonical-name-normalizer Specification

## Purpose
TBD - created by archiving change canonical-name-normalizer-csharp. Update Purpose after archive.
## Requirements
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

### Requirement: POST /admin/canonical/normalize endpoint

`POST /admin/canonical/normalize` SHALL process all canonical JSON files in `data/canonical/` (excluding `.errors.json`, `.warnings.json`, `.progress.json`, `.progress.errors.json` sidecars) and return a normalization report.

The endpoint SHALL accept an optional `?dryRun=true` query parameter. When `dryRun=true`, no files SHALL be written; the response SHALL reflect what would change.

The endpoint SHALL require the `X-Admin-Api-Key` header (same as all admin endpoints).

The response SHALL be HTTP 200 with:
```json
{
  "filesScanned": 2,
  "totalEntities": 1031,
  "dryRun": false,
  "changes": [
    { "file": "tce.json", "titleCased": 226, "flagged": 156, "unchanged": 22 }
  ]
}
```

#### Scenario: Normalize with changes

- **WHEN** `POST /admin/canonical/normalize` is called and canonical files contain ALL-CAPS names
- **THEN** the response SHALL list per-file counts of `titleCased`, `flagged`, and `unchanged`
- **AND** the files SHALL be written with updated names and `needsReview` fields

#### Scenario: Dry run returns counts without writing

- **WHEN** `POST /admin/canonical/normalize?dryRun=true` is called
- **THEN** the response SHALL include `"dryRun": true` and correct per-file counts
- **AND** no files SHALL be modified on disk

#### Scenario: Already-normalized files report zero changes

- **WHEN** all canonical entity names are already title-cased and no OCR artifacts exist
- **THEN** all per-file counts SHALL have `titleCased: 0` and `flagged: 0`

### Requirement: Python script and tests removed

The file `scripts/normalize_canonical_names.py` and `tests/test_normalize_names.py` SHALL be deleted. The project SHALL have no Python runtime dependency.

#### Scenario: No Python files remain

- **WHEN** the implementation is complete
- **THEN** `scripts/normalize_canonical_names.py` SHALL NOT exist in the repository
- **AND** `tests/test_normalize_names.py` SHALL NOT exist in the repository

