## ADDED Requirements
### Requirement: EntityEnvelope carries a NeedsReview flag
`EntityEnvelope` SHALL include a `bool NeedsReview` property (default `false`). This field SHALL be:

- Serialized to and from canonical JSON as `"needsReview": true/false`
- Preserved through `CanonicalJsonLoader`, `EntityMerger`, and ingestion without modification
- Stored in the Qdrant point payload as `needs_review` (bool)

#### Scenario: NeedsReview default is false

- **WHEN** a canonical JSON entity has no `needsReview` field
- **THEN** `EntityEnvelope.NeedsReview` SHALL be `false`

#### Scenario: NeedsReview persists through merge

- **WHEN** `EntityMerger.Merge(canonical, existing)` is called and `canonical.NeedsReview` is `true`
- **THEN** the merged result SHALL have `NeedsReview = true`
### Requirement: Canonical JSON validation reports needsReview entities as warnings
`POST /admin/canonical/validate` SHALL count entities with `needsReview: true` per file and include them in the response warnings. The warning SHALL state the count and instruct the reviewer to fix the name/type/fields and clear the flag before re-ingesting.

Entities with `needsReview: true` SHALL NOT cause validation to return a non-200 status — they are warnings, not errors.

#### Scenario: Validation warns on needsReview entities

- **WHEN** `tce.json` contains 47 entities with `"needsReview": true`
- **THEN** the validation response SHALL include a warning: `{ "file": "tce.json", "type": "needs_review", "count": 47, "message": "47 entities need review..." }`

#### Scenario: Validation with no needsReview entities has no warning

- **WHEN** all entities in all canonical files have `"needsReview": false`
- **THEN** the validation response SHALL contain no `needs_review` warnings

#### Scenario: Ingestion proceeds for needsReview entities

- **WHEN** `POST /admin/books/{id}/ingest-entities` is called and some entities have `needsReview: true`
- **THEN** those entities SHALL be ingested normally — `needsReview` does not block ingestion
