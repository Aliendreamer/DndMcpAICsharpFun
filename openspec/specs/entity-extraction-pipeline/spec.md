# entity-extraction-pipeline

## Purpose

Defines the requirements for the out-of-band LLM-driven entity extraction pipeline. Extraction converts Docling-processed book text into canonical JSON files (`data/canonical/<book-slug>.json`) containing structured entity records. Extraction is decoupled from block ingestion and is triggered explicitly via an admin endpoint.
## Requirements
### Requirement: Entity extraction SHALL be triggered out-of-band via an admin endpoint

The system SHALL expose `POST /admin/books/{id}/extract-entities` which enqueues a one-time entity extraction job for the specified book. The extraction job SHALL NOT run as part of `POST /admin/books/{id}/ingest-blocks`. The handler SHALL return HTTP 202 on enqueue, HTTP 404 when the record is missing, and HTTP 409 when the record's status indicates an extraction or ingestion is already in progress.

#### Scenario: Valid book is enqueued for extraction

- **WHEN** `POST /admin/books/{id}/extract-entities` is called for an existing book whose status is not `Processing`
- **THEN** the system enqueues an entity-extraction work item and returns HTTP 202

#### Scenario: Unknown book returns 404

- **WHEN** the request specifies an id that does not correspond to any record
- **THEN** the system returns HTTP 404 without enqueuing work

#### Scenario: Already-processing book returns 409

- **WHEN** the targeted record's status is `Processing`
- **THEN** the system returns HTTP 409 without enqueuing work

### Requirement: Extraction SHALL produce canonical JSON written to `data/canonical/<book-slug>.json`

The extraction worker SHALL produce a canonical JSON file at `data/canonical/<book-slug>.json` containing the schemaVersion field, a `book` metadata block (display name, source path, edition, hash), and an `entities[]` array of records each conforming to the structured-entities envelope.

#### Scenario: Successful extraction writes canonical JSON

- **WHEN** entity extraction completes for a book
- **THEN** a file is written at `data/canonical/<book-slug>.json` whose contents validate against the canonical-JSON schema and contain at least one entity

#### Scenario: Re-running extraction overwrites the file deterministically

- **WHEN** entity extraction is run twice on the same unchanged book with the same prompt and model
- **THEN** the resulting JSON file is byte-identical (or differs only in declared timestamps) and re-extraction does not duplicate entities

### Requirement: Extraction SHALL consume Docling-extracted blocks, not re-run layout analysis

The extraction worker SHALL reuse the same Docling output that block ingestion uses. It SHALL NOT issue a fresh Docling layout-analysis request for a book that has already been block-extracted in the same session.

#### Scenario: Docling output is reused when available

- **WHEN** entity extraction runs against a book whose Docling output is already cached or persisted from a recent block ingestion
- **THEN** the extractor reads from the cache rather than calling docling-serve

#### Scenario: Docling unavailability fails extraction with a clear error

- **WHEN** Docling output is needed but neither cached nor obtainable
- **THEN** the worker marks the extraction failed with a clear error identifying the missing layout output

### Requirement: LLM extraction SHALL be schema-constrained per entity type

The extraction worker SHALL invoke the LLM separately for each candidate entity (or chunk of related text), passing a **discriminated-union (`oneOf`) JSON schema** as the constraint: a small set of plausible entity-type branches (each with a `const` `entityType` discriminator and that type's fields) plus a `{"entityType":"none","reason":string}` decline branch. The union SHALL be passed through the existing grammar-constrained decoding path (`ChatResponseFormat.ForJsonSchema`); the model selects exactly one branch. The LLM output SHALL be validated against the selected branch before being added to the canonical JSON. Records that fail validation after a bounded retry budget SHALL be written to a `data/canonical/<book-slug>.errors.json` file for human review and SHALL NOT appear in the canonical JSON.

#### Scenario: Branch-conforming LLM output is added to canonical JSON

- **WHEN** the LLM returns JSON that validates against the selected union branch (a typed entity)
- **THEN** the record is added to the canonical JSON's `entities[]` array with the model-selected `entityType`

#### Scenario: Decline branch produces no typed entity

- **WHEN** the LLM selects the `{"entityType":"none"}` branch for a candidate
- **THEN** no typed entity is added to `entities[]`; the candidate is recorded as `Declined` (per the `extraction-disposition` capability)

#### Scenario: Schema-violating LLM output is recorded in errors file

- **WHEN** the LLM returns JSON that does not validate against any union branch after retries
- **THEN** the offending output and validation errors are appended to `data/canonical/<book-slug>.errors.json` and the canonical JSON is unaffected

### Requirement: Extraction SHALL emit progress and a final summary

The worker SHALL log structured progress events (entities-extracted, current type, current page) at a regular cadence and SHALL produce a final summary log entry stating total entities extracted, count per type, and count of validation failures.

#### Scenario: Progress is logged during extraction

- **WHEN** extraction is running
- **THEN** a structured log event is emitted at least every 60 seconds containing current counts and progress percentage

#### Scenario: Final summary is logged on completion

- **WHEN** extraction completes (success or failure)
- **THEN** a single summary log entry includes total entities, per-type counts, validation-failure count, and total elapsed time

### Requirement: Cross-entity reference resolution SHALL distinguish intra-book from inter-book

After candidate entities are produced, the worker SHALL run a reference-resolution pass that classifies every dangling cross-entity reference by whether the target's slug prefix matches the current book's slug prefix.

**Intra-book dangling references** (target slug prefix equals the current book's slug) SHALL be treated as extraction failures: the offending source entity SHALL be excluded from the canonical JSON and recorded in `data/canonical/<book-slug>.errors.json`. The worker MAY retry the affected entities with a corrective prompt up to a bounded retry budget, asking the LLM to either produce the missing target or remove the reference.

**Inter-book dangling references** (target slug prefix differs from the current book's slug) SHALL be recorded in a sibling `data/canonical/<book-slug>.warnings.json` file and SHALL NOT block extraction or affect the canonical JSON.

#### Scenario: All references resolve cleanly

- **WHEN** every cross-entity reference matches a record in the same canonical JSON or has a target outside the current book
- **THEN** the canonical JSON is written and no errors file is produced; the warnings file is either absent or empty

#### Scenario: Intra-book dangling reference excludes the source entity

- **WHEN** a Class entity references a Subclass ID with the same book-slug prefix as the current book and that Subclass entity is not produced after the bounded retry budget
- **THEN** the source Class entity is excluded from the canonical JSON and an entry naming the source entity, the field path, and the missing target ID is appended to `data/canonical/<book-slug>.errors.json`

#### Scenario: Inter-book dangling reference is recorded as a warning

- **WHEN** an entity references an ID whose book-slug prefix differs from the current book's slug
- **THEN** the canonical JSON is written with the entity intact and an entry naming the source entity, the field path, and the missing target ID is appended to `data/canonical/<book-slug>.warnings.json`

### Requirement: Corpus-wide validation endpoint SHALL report cross-book integrity

The system SHALL expose `POST /admin/canonical/validate` which scans every canonical JSON file under `data/canonical/`, loads them through the canonical loader, and runs the cross-entity reference resolver across the union. The endpoint SHALL return HTTP 200 with a structured report when zero FAIL-class issues are found, and HTTP 422 with the same body shape when any FAIL-class issue is present. FAIL-class issues are: schema-version mismatches, duplicate entity IDs across files, and intra-book dangling references that somehow survived extraction. Inter-book dangling references SHALL be reported as warnings (do not contribute to FAIL).

#### Scenario: Clean corpus returns 200 with empty failures

- **WHEN** `POST /admin/canonical/validate` is called against a corpus where every canonical JSON loads cleanly and every cross-entity reference resolves
- **THEN** the response is HTTP 200 with a JSON body whose `failures` array is empty and whose `warnings` array contains any inter-book dangling references

#### Scenario: Schema-version mismatch returns 422

- **WHEN** any canonical JSON file in the corpus has `schemaVersion` other than the loader's `CurrentVersion`
- **THEN** the response is HTTP 422 with a `failures` entry naming the offending file and the schema-version mismatch

#### Scenario: Cross-file duplicate ID returns 422

- **WHEN** two different canonical JSON files contain entities with the same `id`
- **THEN** the response is HTTP 422 with a `failures` entry naming both files and the duplicated id

#### Scenario: Inter-book dangling reference is reported as warning, not failure

- **WHEN** an entity in book A references an ID whose book-slug prefix matches book B but book B's canonical JSON has not yet been ingested into the corpus
- **THEN** the response is HTTP 200 with a `warnings` entry naming the dangling ref, and `failures` is empty

### Requirement: Re-extraction SHALL be idempotent and explicit

Re-running extraction on a book SHALL replace the existing canonical JSON file in full. The system SHALL NOT silently merge new extraction output with hand-corrections in the existing file. The handler SHALL require an explicit `?force=true` query parameter when a canonical JSON already exists for the target book; without it the request SHALL return HTTP 409.

#### Scenario: Re-extraction without force flag is rejected

- **WHEN** `POST /admin/books/{id}/extract-entities` is called for a book that already has a canonical JSON file and `?force=true` is not set
- **THEN** the system returns HTTP 409 and does not enqueue work

#### Scenario: Force re-extraction replaces the canonical JSON

- **WHEN** `POST /admin/books/{id}/extract-entities?force=true` is called
- **THEN** the worker proceeds, fully overwrites the canonical JSON on success, and any prior hand-corrections are lost (the user is responsible for committing them to git first)

### Requirement: Extraction failures SHALL leave the system in a consistent state

If extraction fails for any reason, the canonical JSON file SHALL not be partially written. The system SHALL write to a temp file and rename atomically only on successful completion of the full extraction pass.

#### Scenario: Mid-extraction crash leaves no partial JSON

- **WHEN** extraction crashes or is cancelled before completion
- **THEN** no `data/canonical/<book-slug>.json` file is written, and any temporary files are cleaned up

#### Scenario: Atomic rename on success

- **WHEN** extraction completes successfully
- **THEN** the canonical JSON appears at its final path via a single atomic rename from the temp file

### Requirement: Extraction SHALL produce canonical JSON entities with a `keywords` field for Monster type

When the LLM extraction pipeline processes a Monster entity, the resulting canonical JSON record SHALL include a `keywords` array under `fields` containing the names of notable traits visible in the stat block (e.g. `"Pack Tactics"`, `"Amphibious"`, `"Undead Fortitude"`). The array SHALL be empty when no notable traits are identified. Non-Monster entity types are not required to produce `keywords`.

#### Scenario: Monster extraction produces keywords from trait names

- **WHEN** the LLM extracts a monster whose stat block includes traits named `"Pack Tactics"` and `"Keen Senses"`
- **THEN** the resulting canonical JSON entity has `"fields": { ..., "keywords": ["Pack Tactics", "Keen Senses"] }`

#### Scenario: Monster with no notable traits produces empty keywords

- **WHEN** the LLM extracts a monster with no named traits (e.g. a simple creature)
- **THEN** the resulting canonical JSON entity has `"fields": { ..., "keywords": [] }`

#### Scenario: Non-Monster entities omit keywords field

- **WHEN** the LLM extracts a Spell or Class entity
- **THEN** the resulting canonical JSON entity does not include a `keywords` field under `fields`

### Requirement: LLM extraction produces correct entity type

The model SHALL determine each entity's `EntityType` by selecting the matching branch of the discriminated-union schema — `Class`, `Subclass`, `Spell`, `Monster`, `Feat`, `Item`, `MagicItem`, `Race`, `Subrace`, `Background`, `Rule`, `God`, `Condition`, `DiseasePoison`, `Weapon`, `Armor`, `Trap`, `VehicleMount` — based on the content it reads. The keyword classifier (`HeadingCategoryClassifier`) SHALL NOT determine the final type; it serves only as a prior that prunes the offered branches. When no offered branch fits, the model SHALL select the `none` (decline) branch rather than defaulting to `Class` or fabricating a type.

#### Scenario: Subclass correctly typed

- **WHEN** the LLM extracts an entity for "Circle of Spores" from Tasha's Cauldron of Everything
- **THEN** the extracted entity SHALL have `type: "Subclass"` not `type: "Class"`

#### Scenario: Rule entry correctly typed

- **WHEN** the LLM extracts an entity for "Transmuted Spell" (a metamagic option)
- **THEN** the extracted entity SHALL have `type: "Rule"` not `type: "Class"`

#### Scenario: Unknown content declines instead of defaulting to Class

- **WHEN** the LLM cannot fit a candidate to any offered branch
- **THEN** the LLM SHALL select the `none` (decline) branch, and the candidate SHALL be recorded as `Declined` rather than fabricated as a `Class`

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

### Requirement: Force re-extraction overrides any prior ingestion status
The system SHALL allow `POST /admin/books/{id}/extract-entities?force=true` to proceed regardless of the book's current `IngestionStatus`, including a stuck `EntitiesExtracting` or `EntitiesIngesting` left by an interrupted run. When `force=true`, the pipeline SHALL overwrite any existing canonical JSON for the book and run extraction to completion (or to a resumable checkpoint).

#### Scenario: Force overrides a stuck EntitiesExtracting status
- **WHEN** a book is left in `EntitiesExtracting` with no active run and `extract-entities?force=true` is called
- **THEN** extraction runs and produces `books/canonical/<slug>.json`, and the book's status advances past `EntitiesExtracting`

#### Scenario: Force overwrites existing canonical JSON
- **WHEN** `extract-entities?force=true` is called for a book that already has canonical JSON
- **THEN** the canonical JSON is regenerated and replaced

#### Scenario: Extracted entities become searchable after ingestion
- **WHEN** a book's canonical JSON is ingested via `POST /admin/books/{id}/ingest-entities`
- **THEN** `dnd_entities` contains the book's entity records and entity search returns results for them

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

### Requirement: Same-titled sections far apart yield separate candidates

The candidate scanner SHALL group blocks sharing a section title only when they are within a small page
window of each other; same-titled sections that are far apart (a name reused in different chapters) SHALL
become separate candidates, each keyed on its own page so each is categorized by its own chapter. A
header repeated on an adjacent continuation page MUST still merge into one candidate.

#### Scenario: A name reused across chapters is not merged
- **WHEN** "DARKVISION" appears as a section title at page 184 (an invocation) and again at page 230 (the spell), far beyond the page window
- **THEN** the scanner emits two candidates — one keyed at page 184, one at page 230 — not one merged candidate keyed at page 184

#### Scenario: A continuation-page repeat still merges
- **WHEN** the same section title appears on adjacent pages within the page window
- **THEN** the scanner merges them into one candidate

### Requirement: Silent candidate loss is logged

Candidate scanning SHALL NOT lose a candidate without a trace. The system SHALL log a warning when a
section heading is overwritten by another heading before its section received any body text, and when a
scanned section is skipped because its page resolves to a null/Unknown content category. These logs MUST
NOT change extraction behavior — they only make a previously-silent loss visible.

#### Scenario: A heading overwritten with no body is logged
- **WHEN** a section heading is immediately followed by another heading and the first section received no body text
- **THEN** a warning is logged naming the dropped section title

#### Scenario: A null-category skip is logged
- **WHEN** the scanner skips a section because its page maps to a null/Unknown category
- **THEN** a warning is logged naming the skipped section and page (instead of a silent continue)

