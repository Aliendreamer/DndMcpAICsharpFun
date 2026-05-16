## ADDED Requirements

### Requirement: IngestionRecord stores optional FivetoolsSourceKey
The system SHALL add a nullable `FivetoolsSourceKey` column (max 20 chars) to `IngestionRecords`. Existing rows SHALL default to `null`.

#### Scenario: Book registered with source key

- **WHEN** `POST /admin/books/register` is called with `fivetoolsSourceKey=XDMG`
- **THEN** the created `IngestionRecord` has `FivetoolsSourceKey="XDMG"`

#### Scenario: Book registered without source key

- **WHEN** `POST /admin/books/register` is called without `fivetoolsSourceKey`
- **THEN** the created `IngestionRecord` has `FivetoolsSourceKey=null`

#### Scenario: Invalid source key rejected

- **WHEN** `POST /admin/books/register` is called with a `fivetoolsSourceKey` not present in the registry
- **THEN** the API returns HTTP 422 with a message listing the unknown key

### Requirement: Registration endpoint suggests source key matches
When `fivetoolsSourceKey` is absent from the request, the registration response SHALL include a `suggestedSources` array of up to 3 source keys ranked by name similarity to the submitted `displayName`.

#### Scenario: Suggestion for a known book name

- **WHEN** `POST /admin/books/register` is called with `displayName="Dungeon Master's Guide"` and no source key
- **THEN** the response includes `suggestedSources` containing `"DMG"` and `"XDMG"`

#### Scenario: No suggestions for custom content

- **WHEN** `POST /admin/books/register` is called with `displayName="My Homebrew Compendium"` and no source key
- **THEN** the response includes `suggestedSources` as an empty array or low-confidence results

### Requirement: GET /admin/5etools/sources lists valid source keys
The system SHALL expose `GET /admin/5etools/sources` returning all entries from the registry with `id`, `name`, `group`, `publishedYear`, and `displayAbbr` fields.

#### Scenario: Full source list returned

- **WHEN** `GET /admin/5etools/sources` is called
- **THEN** returns HTTP 200 with a JSON array of all ~60 source entries

#### Scenario: Optional group filter

- **WHEN** `GET /admin/5etools/sources?group=core` is called
- **THEN** returns only entries with `group="core"`
