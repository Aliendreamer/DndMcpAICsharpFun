# http-contracts (delta)

## ADDED Requirements

### Requirement: .http file documents the bookType field on register and retrieval
The `DndMcpAICsharpFun.http` file SHALL include the `bookType` form field on the `POST /admin/books/register` example with a comment listing the valid values, and SHALL include at least one retrieval example using the `bookType` query parameter to demonstrate filtering.

#### Scenario: Register example documents bookType
- **WHEN** the `.http` file is opened
- **THEN** the register block includes a `bookType` form part and a comment line listing `Core | Supplement | Adventure | Setting | Unknown`

#### Scenario: Retrieval example demonstrates bookType filter
- **WHEN** the retrieval section of the `.http` file is opened
- **THEN** at least one example URL includes `&bookType=...` to show the filter in action
