## MODIFIED Requirements

### Requirement: Deterministic point IDs
`IEntityVectorStore.UpsertAsync` SHALL write Qdrant points with UUIDs derived deterministically from each entity's `id` string (UUID v5, DNS namespace). Random GUIDs SHALL NOT be used.

#### Scenario: Re-ingest does not create duplicates

- **WHEN** `ingest-entities` is run twice for the same book with the same canonical data
- **THEN** the Qdrant collection SHALL contain the same number of points after both runs (second run overwrites first)

### Requirement: Batch entity fetch by ID list
`IEntityVectorStore` SHALL expose `GetByIdsAsync(IList<string> entityIds, CancellationToken ct)` returning `IReadOnlyDictionary<string, EntityEnvelope>` — a map from entity ID to its current stored envelope. Entity IDs not found in the collection SHALL be absent from the result dictionary.

#### Scenario: Known IDs are returned

- **WHEN** `GetByIdsAsync(["tce.subclass.circle-of-spores"])` is called and that entity exists
- **THEN** the result SHALL contain one entry with key `"tce.subclass.circle-of-spores"`

#### Scenario: Unknown IDs are absent

- **WHEN** `GetByIdsAsync(["nonexistent.class.foo"])` is called
- **THEN** the result SHALL be an empty dictionary

#### Scenario: Large batch is handled

- **WHEN** `GetByIdsAsync` is called with 500 entity IDs
- **THEN** all matching entities SHALL be returned without truncation
