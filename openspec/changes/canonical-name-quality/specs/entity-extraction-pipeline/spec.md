## MODIFIED Requirements

### Requirement: Extraction prompt produces title-case entity names
The extraction system prompt SHALL include the rule: entity names MUST be output in title case following D&D conventions — capitalize all words except articles and prepositions (`of, the, a, an, in, on, at, to, and, or, but, for, nor`) unless they appear at the start of the name. ALL-CAPS names as they appear in PDF headings MUST be converted.

#### Scenario: LLM outputs title-case name
- **WHEN** the source PDF contains the heading `CIRCLE OF SPORES`
- **THEN** the extracted entity `name` SHALL be `"Circle of Spores"`, not `"CIRCLE OF SPORES"`

### Requirement: Extraction schema includes a per-entity confidence field
The JSON schema provided to the LLM for entity extraction SHALL include a `confidence` field with allowed values `"low"`, `"medium"`, or `"high"`. The LLM SHALL set this field based on how cleanly it could identify the entity's name, type, and canonical content from the PDF text.

- `"high"`: name, type, and content are unambiguous
- `"medium"`: one of name/type/content required a judgment call
- `"low"`: significant ambiguity; the entity may be misidentified or incomplete

The `confidence` field is used only during post-processing and SHALL NOT be persisted to the canonical JSON.

#### Scenario: Low-confidence entity flagged for review
- **WHEN** the LLM extracts an entity and outputs `"confidence": "low"`
- **THEN** the post-processing step SHALL set `needsReview: true` on that entity

#### Scenario: High-confidence entity not flagged by confidence alone
- **WHEN** the LLM extracts an entity and outputs `"confidence": "high"`
- **THEN** the `needsReview` flag SHALL NOT be set by the confidence check (heuristic may still flag it)

### Requirement: Post-processing applies OCR-artifact heuristic to entity names
After LLM extraction, a post-processing step SHALL inspect each entity's `name` field and set `needsReview = true` if any of the following are true:
1. `name.isupper()` is true and `len(name) > 1` (all-caps — LLM ignored the naming rule)
2. The lowercased name matches the regex `\b[a-z] [a-z]\b` (letter-space-letter split word)
3. The name contains `\.{3,}` or other noise sequences of repeated punctuation
4. A single word in the name has more than 3 upper/lower case alternations (e.g., `WoRLd`)

#### Scenario: All-caps name after extraction is flagged
- **WHEN** an extracted entity has `"name": "BESTIAL SOUL"` (LLM did not follow naming rule)
- **THEN** `needsReview` SHALL be set to `true`

#### Scenario: Split-word OCR artifact is flagged
- **WHEN** an extracted entity has `"name": "Path of the Beast f eature"`
- **THEN** `needsReview` SHALL be set to `true`

#### Scenario: Clean title-case name passes heuristic
- **WHEN** an extracted entity has `"name": "Circle of Spores"` and `"confidence": "high"`
- **THEN** `needsReview` SHALL remain `false`
