## ADDED Requirements

### Requirement: Extraction SHALL deterministically normalize entity names

After LLM extraction and before an entity is built, the extraction pipeline SHALL deterministically normalize each entity's `name` using `EntityNameNormalizer`. A name that is all-caps AND has no other OCR artifact (split-word, noise, or case-alternation, as detected by `ExtractionNeedsReview.HasOcrArtifacts` on the lowercased name) SHALL be converted to D&D title case; a curated allowlist of D&D acronyms SHALL be preserved in their canonical casing (`NPC`, `NPCs`, `PC`, `PCs`, `DM`, `GP`, `SP`, `CP`, `PP`, `EP`, `XP`, `HP`, `AC`, `DC`, `CR`, `AoE`, `D&D`). A genuinely garbled name SHALL be left unchanged so the artifact heuristic can still detect it. The resulting name SHALL be the value written to canonical JSON and passed to the OCR-artifact heuristic. Normalization SHALL NOT depend on the LLM obeying a prompt rule.

#### Scenario: All-caps heading is normalized at extraction

- **WHEN** the LLM emits an entity with `"name": "DECK OF MANY THINGS"`
- **THEN** the canonical entity `name` SHALL be `"Deck of Many Things"`

#### Scenario: D&D acronym is preserved

- **WHEN** the LLM emits an entity with `"name": "750 GP ART OBJECTS"`
- **THEN** the canonical entity `name` SHALL be `"750 GP Art Objects"`, not `"750 Gp Art Objects"`

#### Scenario: Already-clean name is unchanged

- **WHEN** the LLM emits an entity with `"name": "Circle of Spores"`
- **THEN** the canonical entity `name` SHALL remain `"Circle of Spores"`

## MODIFIED Requirements

### Requirement: Post-processing applies OCR-artifact heuristic to entity names
After LLM extraction, entity names are normalized by `EntityNameNormalizer` (see "Extraction SHALL deterministically normalize entity names"). The OCR-artifact heuristic SHALL then inspect the **normalized** `name` and set `needsReview = true` if any of the following are true:

1. The lowercased name matches the regex `\b[a-z] [a-z]\b` (letter-space-letter split word)
2. The name contains `\.{3,}` or other noise sequences of repeated punctuation
3. A single word in the name has more than 3 upper/lower case alternations (e.g., `WoRLd`)

Because names are normalized before this step, an all-caps heading name does not by itself set `needsReview`; it is converted to title case instead.

#### Scenario: All-caps name is normalized, not flagged

- **WHEN** the LLM emits an entity with `"name": "BESTIAL SOUL"` and `"confidence": "high"`
- **THEN** the canonical entity `name` SHALL be `"Bestial Soul"` and `needsReview` SHALL remain `false`

#### Scenario: Split-word OCR artifact is flagged

- **WHEN** an extracted entity has `"name": "Path of the Beast f eature"`
- **THEN** `needsReview` SHALL be set to `true`

#### Scenario: Clean title-case name passes heuristic

- **WHEN** an extracted entity has `"name": "Circle of Spores"` and `"confidence": "high"`
- **THEN** `needsReview` SHALL remain `false`
