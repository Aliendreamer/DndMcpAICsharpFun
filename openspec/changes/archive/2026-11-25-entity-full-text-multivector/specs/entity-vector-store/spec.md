## MODIFIED Requirements

### Requirement: Entity ingestion SHALL embed `canonicalText` and store the full envelope as payload

For each entity in a canonical JSON file, the ingestion worker SHALL embed the pieces of the entity's `canonicalText` and upsert one Qdrant point into `dnd_entities` that holds one vector per piece (multivector, MaxSim). The point's payload SHALL include all envelope fields (id, type, name, sourceBook, edition, page, firstAppearedIn, revisedIn, settingTags, canonicalText, keywords) and a flattened/indexed subset of type-specific fields suitable for filtering (e.g. `crNumeric`, `spellLevel`, `damageType`, `keywords[]`). The `keywords` payload field SHALL be written as a repeated keyword value (one entry per keyword string) so the Qdrant keyword index can match individual values.

#### Scenario: Entity is embedded once and upserted with full payload including keywords

- **WHEN** a canonical JSON file with N entities is ingested and some entities have non-empty `keywords`
- **THEN** exactly N points exist in `dnd_entities`, each carrying the envelope fields plus indexable type-specific fields, and points with keywords have the `keywords` payload field populated

#### Scenario: Entity with empty keywords is upserted without keywords payload field

- **WHEN** an entity has `keywords = []`
- **THEN** the Qdrant point is upserted without a `keywords` payload key (or with an empty array), and keyword filter queries do not match it

#### Scenario: Re-ingesting a canonical JSON deletes prior points before upserting

- **WHEN** entity ingestion runs twice on the same canonical JSON
- **THEN** points from the prior run for that book are deleted before the new ones are upserted; total point count for that book equals N (no duplicates)

#### Scenario: The best piece decides the score

- **WHEN** a query vector equals one of an entity's piece vectors
- **THEN** that entity SHALL score as if that piece were its only vector
