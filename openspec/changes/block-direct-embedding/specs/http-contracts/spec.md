# http-contracts (delta)

## ADDED Requirements

### Requirement: DndMcpAICsharpFun.http documents the block-ingest endpoint
The `DndMcpAICsharpFun.http` file SHALL contain an example request for `POST /admin/books/{id}/ingest-blocks` with the `X-Admin-Api-Key` header and a brief comment explaining the difference from `/extract` + `/ingest-json`. The file SHALL also document the `Retrieval:Collection` configuration switch in a comment near the retrieval examples.

#### Scenario: Block-ingest example exists
- **WHEN** the `.http` file is opened
- **THEN** there is at least one block whose first non-comment line begins with `POST {{baseUrl}}/admin/books/<id>/ingest-blocks`

#### Scenario: Collection-switch comment exists
- **WHEN** the retrieval section of the `.http` file is read
- **THEN** there is a comment line explaining that `Retrieval:Collection` selects between `chunks` and `blocks`
