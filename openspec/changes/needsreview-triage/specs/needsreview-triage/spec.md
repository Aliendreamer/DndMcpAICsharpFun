## ADDED Requirements

### Requirement: List needsReview entities with derived reason

`GET /admin/entities/needs-review` SHALL return the entities with `needsReview == true`, read from the canonical files, with optional `book` (slug) and `reason` filters and `offset`/`limit` paging. Each item SHALL include `id`, `name`, `book`, `type`, `page`, and a derived `reason` that is `"ocr-artifact"` when `ExtractionNeedsReview.HasOcrArtifacts(name)` is true, otherwise `"low-confidence"`. The response SHALL include the total count for the filter.

#### Scenario: List returns flagged entities with reason

- **WHEN** `GET /admin/entities/needs-review` is called and a flagged entity has an all-clean name flagged for confidence
- **THEN** that entity SHALL appear with `reason: "low-confidence"`

#### Scenario: Reason is ocr-artifact for garbled names

- **WHEN** a flagged entity's name still has an OCR artifact (e.g. a split-letter word)
- **THEN** it SHALL appear with `reason: "ocr-artifact"`

#### Scenario: Filter by book and page

- **WHEN** `GET /admin/entities/needs-review?book=phb14&offset=0&limit=50` is called
- **THEN** only `phb14` flagged entities SHALL be returned, at most 50, with the total count for `phb14`

### Requirement: Resolve a flagged entity, canonical-backed

`POST /admin/entities/{id}/resolve` SHALL accept `{ action: "accept" | "edit", name?, fields? }`. For `accept`, the system SHALL clear `needsReview` on that entity in its canonical file. For `edit`, the system SHALL apply the provided `name` and/or `fields` and clear `needsReview`. After writing the canonical file, the system SHALL re-project only that entity into Qdrant (targeted re-index), leaving all other points untouched. Resolving an entity that is already resolved SHALL succeed as a no-op.

#### Scenario: Accept clears the flag in canonical and Qdrant

- **WHEN** `POST /admin/entities/{id}/resolve { "action": "accept" }` is called
- **THEN** the entity's `needsReview` SHALL be `false` in its canonical file
- **THEN** the entity's Qdrant point SHALL have `needsReview` `false`
- **THEN** no other entity's Qdrant point SHALL be modified

#### Scenario: Edit applies changes and clears the flag

- **WHEN** `POST /admin/entities/{id}/resolve { "action": "edit", "name": "Finger of Death" }` is called
- **THEN** the entity's name in canonical SHALL be `"Finger of Death"` and `needsReview` SHALL be `false`
- **THEN** the entity's Qdrant point SHALL reflect the new name

#### Scenario: Unknown entity id

- **WHEN** resolve is called for an id not present in any canonical file
- **THEN** the response SHALL be 404 Not Found

### Requirement: Bulk-accept a needsReview set

`POST /admin/entities/needs-review/accept` SHALL accept `{ book?, reason? }` and clear `needsReview` for every entity matching the filter, writing each affected canonical file and re-indexing each affected entity. The response SHALL report the number of entities cleared.

#### Scenario: Bulk accept a book's low-confidence entities

- **WHEN** `POST /admin/entities/needs-review/accept { "book": "mm14", "reason": "low-confidence" }` is called
- **THEN** every `mm14` entity flagged for low-confidence SHALL have `needsReview` cleared in canonical and Qdrant
- **THEN** the response SHALL report the count cleared

### Requirement: Targeted single-entity re-index

The system SHALL provide a targeted re-index that re-projects exactly one entity (by book + id) into Qdrant — loading it from canonical, applying the same merge/render/embed as full ingestion, and upserting that single point under the book's FileHash — without deleting or modifying any other point.

#### Scenario: Re-index touches exactly one point

- **WHEN** a single entity is re-indexed
- **THEN** exactly one Qdrant point (that entity) SHALL be upserted
- **THEN** the whole-book stale-cleanup (`DeleteByFileHashExceptAsync`) SHALL NOT be invoked
