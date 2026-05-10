## ADDED Requirements

### Requirement: Fix-types admin endpoint
The system SHALL expose `POST /admin/canonical/fix-types` that corrects entity types and IDs in an existing canonical JSON file by cross-referencing 5etools data.

Query parameters:
- `book` (required) — canonical book slug (e.g. `tce`, `phb14`, `dmg14`)

#### Scenario: Successful type fix
- **WHEN** `POST /admin/canonical/fix-types?book=tce` is called
- **THEN** the system SHALL load `data/canonical/tce.json`, match each entity against 5etools data by `name + sourceBook`, assign the correct `EntityType`, rewrite the entity `id` to use the new type slug, update all internal cross-reference strings that contained the old id, and save the corrected file in place
- **THEN** the response SHALL be `200 OK` with a summary of entities fixed, entities unmatched, and cross-references updated

#### Scenario: Entity with no 5etools match
- **WHEN** a canonical entity has no matching 5etools entry (by name + sourceBook)
- **THEN** the entity SHALL be left unchanged (type and ID preserved as-is)
- **THEN** the response summary SHALL include the unmatched entity in an `unmatched` list

#### Scenario: Book not found
- **WHEN** `POST /admin/canonical/fix-types?book=nonexistent` is called
- **THEN** the system SHALL return `404 Not Found`

#### Scenario: Idempotent operation
- **WHEN** `POST /admin/canonical/fix-types?book=tce` is called twice on an already-corrected file
- **THEN** the second call SHALL make no changes and SHALL report 0 entities fixed

### Requirement: Internal cross-reference rewriting
When an entity ID is rewritten by fix-types, all occurrences of the old ID string within the same canonical JSON file's entity `fields` SHALL be updated to the new ID.

#### Scenario: Cross-reference updated
- **WHEN** entity `tce.class.foo` is renamed to `tce.subclass.foo`
- **THEN** any `fields` value in any entity containing the string `"tce.class.foo"` SHALL be rewritten to `"tce.subclass.foo"`
