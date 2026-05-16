## ADDED Requirements

### Requirement: Retrieval surface SHALL expose entity-aware endpoints in addition to block search

The system SHALL expose entity-aware retrieval endpoints alongside the existing `/retrieval/search` block endpoint:

- `GET /retrieval/entities/{id}` — return one entity by ID
- `GET /retrieval/entities/search` — vector search over entities with structured filters
- `GET /admin/retrieval/entities/search` — admin diagnostic with full `fields` block and Qdrant point ID

The existing `/retrieval/search` and `/admin/retrieval/search` block endpoints SHALL continue to operate against `dnd_blocks` exactly as before.

#### Scenario: Block retrieval is unchanged after this change

- **WHEN** `GET /retrieval/search?q=fireball` is called after the entity capability ships
- **THEN** the response and behaviour are identical to before — querying `dnd_blocks`, returning block records ordered by score, with the same filter parameters

#### Scenario: Entity retrieval endpoints are reachable

- **WHEN** any entity retrieval endpoint is called against a populated entity collection
- **THEN** the endpoint responds with entity data, not block data

### Requirement: Entity vector search SHALL apply structured filters server-side

The `/retrieval/entities/search` endpoint SHALL accept the following query parameters and apply them as Qdrant payload filters: `type`, `sourceBook`, `edition`, `bookType`, `settingTag`, `keyword`, `crNumeric_lte`, `crNumeric_gte`, `spellLevel`, `damageType`. Multiple filters SHALL compose with AND semantics. Unknown values SHALL be silently dropped (consistent with the block-search filter behaviour).

#### Scenario: Type filter restricts to a single entity type

- **WHEN** `?q=fire&type=Spell` is called
- **THEN** every result has `type == "Spell"` regardless of vector similarity

#### Scenario: Composing filters narrows results

- **WHEN** `?q=fire&type=Spell&spellLevel=3` is called
- **THEN** every result has both `type == "Spell"` and `spellLevel == 3`

#### Scenario: Unknown type is dropped silently

- **WHEN** `type=NotARealType` is set
- **THEN** the filter is dropped and the search proceeds without it

### Requirement: Entity-by-ID retrieval SHALL return the full structured fields

`GET /retrieval/entities/{id}` SHALL return the full entity record including all envelope fields and the complete `fields` block. The vector-search endpoint SHALL return only the envelope and a snippet (no full `fields`); callers fetch full data by ID when needed.

#### Scenario: Search returns envelopes only; ID lookup returns full fields

- **WHEN** an entity appears in both a `/retrieval/entities/search` result and a subsequent `/retrieval/entities/{id}` lookup
- **THEN** the search result contains the envelope without `fields`; the by-ID response contains the envelope plus the full `fields` block
