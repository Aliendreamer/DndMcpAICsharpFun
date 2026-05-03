# http-contracts (delta)

## ADDED Requirements

### Requirement: .http register example omits sourceName
The `DndMcpAICsharpFun.http` file's `POST /admin/books/register` example SHALL NOT contain a `sourceName` form part. The example SHALL include `version`, `displayName`, and `bookType` parts plus the file part.

#### Scenario: Register example does not document sourceName
- **WHEN** the `.http` file is searched for `name="sourceName"`
- **THEN** no match is found
