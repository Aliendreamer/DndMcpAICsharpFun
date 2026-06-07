## ADDED Requirements
### Requirement: Book registration accepts optional 5etools source key
The registration form SHALL accept an optional `fivetoolsSourceKey` multipart field. When provided, it SHALL be validated against `BookSourceRegistry` and stored on `IngestionRecord`. When absent, the response SHALL include `suggestedSources` as defined in the `fivetools-source-key` spec.

#### Scenario: Valid source key stored on registration

- **WHEN** `POST /admin/books/register` includes `fivetoolsSourceKey=PHB`
- **THEN** `IngestionRecord.FivetoolsSourceKey` is set to `"PHB"` and HTTP 200 is returned

#### Scenario: Unknown source key rejected on registration

- **WHEN** `POST /admin/books/register` includes `fivetoolsSourceKey=NOTABOOK`
- **THEN** HTTP 422 is returned with an error identifying the unknown key

#### Scenario: Missing source key succeeds with suggestions

- **WHEN** `POST /admin/books/register` is called without `fivetoolsSourceKey`
- **THEN** HTTP 200 is returned and the response body includes `suggestedSources`

### Requirement: Entity ingestion normalises sourceBook to source key
When ingesting entities from canonical JSON into `dnd_entities`, if the source book's `IngestionRecord` has a non-null `FivetoolsSourceKey`, the Qdrant payload `sourceBook` field SHALL be set to that key (not the display name).

#### Scenario: Canonical entity from bound book

- **WHEN** ingesting a canonical entity whose book has `FivetoolsSourceKey="XDMG"`
- **THEN** the Qdrant entity point has `sourceBook="XDMG"` in its payload

#### Scenario: Canonical entity from unbound book

- **WHEN** ingesting a canonical entity whose book has `FivetoolsSourceKey=null`
- **THEN** the Qdrant entity point uses the existing `sourceBook` value (display name or empty)
