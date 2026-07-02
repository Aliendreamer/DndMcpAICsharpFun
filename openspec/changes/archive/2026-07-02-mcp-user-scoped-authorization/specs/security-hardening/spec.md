## ADDED Requirements

### Requirement: Character-scoped resolution authorizes by caller identity

Character-scoped resolution SHALL authorize by the calling user's identity. `resolve_character_feature`
(or its replacement) SHALL resolve only hero snapshots owned by the signed-in user, verified through
the snapshot → hero → campaign → user ownership chain, and SHALL deny or return no data for a snapshot
id the caller does not own. (SEC-08)

#### Scenario: Owner resolves their own snapshot
- **WHEN** a signed-in user resolves a feature for a snapshot they own
- **THEN** the resolution succeeds

#### Scenario: Cross-tenant snapshot id is denied
- **WHEN** a caller requests a snapshot id owned by a different user
- **THEN** the resolution returns no character data (authorization failure), not the other user's facts

#### Scenario: Iterating ids does not leak data
- **WHEN** a caller iterates arbitrary snapshot ids
- **THEN** only snapshots they own resolve; all others are denied
