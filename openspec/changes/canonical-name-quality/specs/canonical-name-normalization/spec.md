## ADDED Requirements

### Requirement: One-time normalization script converts all-caps entity names to title case
A script at `scripts/normalize_canonical_names.py` SHALL process canonical JSON files and:

- Convert entity `name` fields that are entirely uppercase (`name.isupper()` is true) to title case using the D&D title-case algorithm (capitalize all words except articles/prepositions `of, the, a, an, in, on, at, to, and, or, but, for, nor` unless the word starts the name; correct `'S` → `'s` after apostrophes).
- Set `"needsReview": true` on any entity whose name matches the OCR-artifact heuristic (see structured-entities spec).
- Leave names that are already mixed-case and pass the heuristic unchanged.
- Be idempotent: running twice produces the same output.

#### Scenario: All-caps name is normalized

- **WHEN** the script processes an entity with `"name": "CIRCLE OF SPORES"`
- **THEN** the entity name SHALL become `"Circle of Spores"` and `needsReview` SHALL remain `false`

#### Scenario: Garbled OCR name is flagged

- **WHEN** the script processes an entity with `"name": "3rd-level Path of the Beast f eature"`
- **THEN** the name SHALL be left unchanged and `"needsReview": true` SHALL be set

#### Scenario: Mixed-case clean name is untouched

- **WHEN** the script processes an entity with `"name": "Fighter"` (already correct)
- **THEN** the name SHALL remain `"Fighter"` and `needsReview` SHALL remain `false`

#### Scenario: Script is idempotent

- **WHEN** the script is run twice on the same file
- **THEN** the output SHALL be identical after both runs

### Requirement: Title-case algorithm handles D&D naming conventions
The title-case conversion SHALL:

- Lowercase the set `{of, the, a, an, in, on, at, to, and, or, but, for, nor}` except when the word is first in the name.
- Correct `'S` (apostrophe-S in all-caps form) to `'s`.
- Capitalize the first letter after a hyphen.

#### Scenario: Small word lowercased

- **WHEN** converting `"CIRCLE OF SPORES"`
- **THEN** the result SHALL be `"Circle of Spores"` (not `"Circle Of Spores"`)

#### Scenario: Apostrophe corrected

- **WHEN** converting `"TASHA'S CAULDRON"`
- **THEN** the result SHALL be `"Tasha's Cauldron"` (not `"Tasha'S Cauldron"`)
