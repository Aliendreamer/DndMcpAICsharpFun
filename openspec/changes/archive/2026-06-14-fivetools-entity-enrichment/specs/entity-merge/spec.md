## MODIFIED Requirements

### Requirement: Per-field merge during canonical ingest
When `ingest-entities` runs and a 5etools enrichment record (or existing Qdrant point) exists for an entity ID, the system SHALL merge the canonical and existing data using per-field priority rules before upserting.

Field priority rules:

- `fields` → **deep per-key merge**: our narrative keys are preserved, while 5etools wins clean structured/scalar values. A key is "narrative" if it is in the per-entity-type narrative allowlist (e.g. `entries`, `description`, `text`, and per-type prose keys); for narrative keys the canonical (LLM) value wins. For all other keys (scalars, numbers, enums, and non-narrative arrays such as `cr`, `level`, `ac`, `components`, `traitTags`, `type`), the existing (5etools) value wins **when present and non-empty**; if the existing side lacks the key, the canonical value is kept. Objects are merged recursively by the same rules.
- `canonicalText`, `firstAppearedIn` → canonical always wins
- `name` → existing (5etools) clean name wins when an existing record is present AND canonical `DataSource` is not `"manual"`; otherwise canonical wins
- `type` → existing (5etools) wins if present and canonical type is `Class`
- `srd`, `srd52`, `basicRules2024` → existing (5etools) wins
- `keywords` → union of both lists (existing 5etools trait-tags plus canonical)
- `page` → existing wins if set; canonical wins if existing has no page
- `DataSource` → always set to `"llm"` after merge (unless canonical was `"manual"`, which is preserved)

#### Scenario: 5etools structured value replaces OCR-noisy canonical value

- **WHEN** the canonical entity has `fields.cr = "l/4"` (OCR noise) and the 5etools record has `fields.cr = "1/4"`
- **THEN** the merged `fields.cr` SHALL be `"1/4"`

#### Scenario: Our narrative entries are preserved over 5etools

- **WHEN** both sides have `fields.entries` and they differ
- **THEN** the merged `fields.entries` SHALL be the canonical (LLM) value

#### Scenario: 5etools fills a structured field we lack

- **WHEN** the canonical entity has no `fields.components` and the 5etools record has `fields.components`
- **THEN** the merged `fields.components` SHALL be the 5etools value

#### Scenario: Canonical ingested after 5etools keeps SRD flags

- **WHEN** a canonical entity is ingested and a 5etools record exists with the same entity ID
- **THEN** the merged point SHALL have 5etools `srd`, `srd52`, `basicRules2024` flags
- **THEN** the merged point `DataSource` SHALL be `"llm"`

#### Scenario: Manual name is protected

- **WHEN** the canonical entity has `DataSource == "manual"` and a 5etools record exists with a different name
- **THEN** the merged `name` SHALL be the canonical (manual) name

#### Scenario: No existing record

- **WHEN** a canonical entity is ingested and no 5etools record (and no existing Qdrant point) exists for that entity ID
- **THEN** the canonical entity SHALL be upserted as-is without any merge

#### Scenario: Keyword merge unions both lists

- **WHEN** the existing record has `keywords: ["fiend", "demon"]` and canonical has `keywords: ["demon", "shapechanger"]`
- **THEN** the merged point SHALL have `keywords` containing `fiend`, `demon`, and `shapechanger`
