# entity-vector-store

## Purpose

Defines the requirements for the Qdrant entity collection (`dnd_entities`): its creation, payload schema, indexes, entity ingestion, retrieval-by-ID, and entity vector search. The entity collection is independent of the blocks collection (`dnd_blocks`) and stores one point per structured entity record.

## Requirements

### Requirement: A separate Qdrant collection SHALL store entity embeddings

The system SHALL create and use a Qdrant collection named by configuration `Qdrant:EntitiesCollectionName` (default `dnd_entities`). This collection SHALL be distinct from `dnd_blocks` and SHALL NOT be used for block-level retrieval.

#### Scenario: Both collections exist after startup
- **WHEN** the application starts against an empty Qdrant instance and runs collection bootstrap
- **THEN** Qdrant contains both `dnd_blocks` (existing) and `dnd_entities` (new) with the configured vector dimension

#### Scenario: Entity-collection name is configurable
- **WHEN** `Qdrant:EntitiesCollectionName` is set to a non-default value
- **THEN** all entity ingestion and retrieval uses the configured name

### Requirement: Entity ingestion SHALL embed `canonicalText` and store the full envelope as payload

For each entity in a canonical JSON file, the ingestion worker SHALL embed the entity's `canonicalText` and upsert one Qdrant point into `dnd_entities`. The point's payload SHALL include all envelope fields (id, type, name, sourceBook, edition, page, firstAppearedIn, revisedIn, settingTags, canonicalText) and a flattened/indexed subset of type-specific fields suitable for filtering (e.g. `crNumeric`, `spellLevel`, `damageType`, `keywords[]`).

#### Scenario: Entity is embedded once and upserted with full payload
- **WHEN** a canonical JSON file with N entities is ingested
- **THEN** exactly N points exist in `dnd_entities` and each carries the envelope fields plus indexable type-specific fields

#### Scenario: Re-ingesting a canonical JSON deletes prior points before upserting
- **WHEN** entity ingestion runs twice on the same canonical JSON
- **THEN** points from the prior run for that book are deleted before the new ones are upserted; total point count for that book equals N (no duplicates)

### Requirement: Entity ingestion SHALL be triggered explicitly per book

The system SHALL expose `POST /admin/books/{id}/ingest-entities` which reads the canonical JSON for the book at `data/canonical/<book-slug>.json` and ingests all entities into `dnd_entities`. The handler SHALL return HTTP 202 on enqueue, HTTP 404 when the record or canonical JSON is missing, and HTTP 409 when the record is `Processing`.

#### Scenario: Valid book with canonical JSON is enqueued
- **WHEN** the canonical JSON exists for the book and the record is not Processing
- **THEN** the system enqueues an entity-ingestion work item and returns HTTP 202

#### Scenario: Missing canonical JSON returns 404
- **WHEN** no `data/canonical/<book-slug>.json` exists for the book
- **THEN** the system returns HTTP 404 with an error indicating the canonical JSON is required

### Requirement: Entity payload fields SHALL have Qdrant payload indexes

The system SHALL create Qdrant payload indexes for fields used in filter queries: `type` (keyword), `sourceBook` (keyword), `edition` (keyword), `bookType` (keyword), `settingTags` (keyword), `keywords` (keyword), `crNumeric` (float), `spellLevel` (integer), `damageType` (keyword). Indexes SHALL be created during collection bootstrap.

#### Scenario: Indexes exist after collection bootstrap
- **WHEN** the application bootstraps the entity collection
- **THEN** payload indexes for the listed fields are created (or already present) before any retrieval queries are accepted

### Requirement: Retrieval by entity ID SHALL return the full record

The system SHALL expose `GET /retrieval/entities/{id}` which returns the full entity envelope (including the full `fields` block) for the entity whose `id` matches. The endpoint SHALL return HTTP 404 if no entity with that ID exists.

#### Scenario: Existing ID returns the full envelope
- **WHEN** `GET /retrieval/entities/phb14.class.fighter` is called against a populated entity collection
- **THEN** the response is HTTP 200 with a JSON body containing all envelope fields and the full `fields` block

#### Scenario: Unknown ID returns 404
- **WHEN** the requested id does not correspond to any entity in the collection
- **THEN** the response is HTTP 404

### Requirement: Vector search over entities SHALL be exposed at a dedicated endpoint

The system SHALL expose `GET /retrieval/entities/search?q=<text>` which embeds the query and returns the top-K nearest entities. Optional filter query parameters `type`, `sourceBook`, `edition`, `settingTag`, `keyword`, `crNumeric_lte`, `crNumeric_gte`, `spellLevel`, `damageType` SHALL apply Qdrant payload filters. The result SHALL include each match's envelope (without the full `fields` block — call `/retrieval/entities/{id}` for full details) and the similarity score. Result count SHALL be bounded by `topK` (default 10) and capped at `Retrieval:MaxTopK`.

#### Scenario: Plain query returns ranked entities
- **WHEN** `GET /retrieval/entities/search?q=swashbuckler` is called
- **THEN** the response is HTTP 200 with a JSON array of entity envelopes ordered by descending similarity, each containing `id`, `type`, `name`, `sourceBook`, `edition`, `score`, and a short text snippet

#### Scenario: Filters compose
- **WHEN** `?q=evocation&type=Spell&spellLevel=3` is set
- **THEN** all returned entities have `type == Spell` and `spellLevel == 3`

#### Scenario: Numeric range filter on crNumeric
- **WHEN** `?type=Monster&crNumeric_lte=5&crNumeric_gte=3` is set
- **THEN** all returned entities have `crNumeric` between 3 and 5 inclusive

#### Scenario: Keyword filter powers descriptive queries
- **WHEN** `?type=Monster&keyword=amphibian` is set
- **THEN** all returned entities have `"amphibian"` in their `keywords` array

### Requirement: An admin diagnostic endpoint SHALL return entity-search results with point IDs

The system SHALL expose `GET /admin/retrieval/entities/search` returning the same data as the public endpoint plus the Qdrant point ID and the full structured `fields` block for each result. This endpoint SHALL require the admin API key.

#### Scenario: Diagnostic search includes pointId and fields
- **WHEN** `GET /admin/retrieval/entities/search?q=fireball` is called with a valid admin key
- **THEN** each result contains a non-empty `pointId` and a populated `fields` object

### Requirement: Block index and entity index SHALL remain independent

The system SHALL NOT mix entity points and block points in the same Qdrant collection. Block-level retrieval (`/retrieval/search`) SHALL continue to query only `dnd_blocks` and SHALL NOT be affected by changes to the entity collection.

#### Scenario: Block search ignores the entity collection
- **WHEN** `GET /retrieval/search?q=fireball` is called against a populated entity collection
- **THEN** the search queries only `dnd_blocks` and returns block records, not entity records

#### Scenario: Entity search ignores the block collection
- **WHEN** `GET /retrieval/entities/search?q=fireball` is called against a populated block collection
- **THEN** the search queries only `dnd_entities` and returns entity records, not block records
