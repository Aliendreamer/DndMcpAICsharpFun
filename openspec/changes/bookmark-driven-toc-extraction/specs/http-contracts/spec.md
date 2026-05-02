# http-contracts (delta)

## ADDED Requirements

### Requirement: DndMcpAICsharpFun.http reflects the post-tocPage register form and excludes debug-toc
The `DndMcpAICsharpFun.http` file SHALL show `POST /admin/books/register` with form fields `sourceName`, `version`, and `displayName` only — no `tocPage` field — and SHALL NOT contain any example for `POST /admin/books/{id}/debug-toc` or `GET /admin/books/{id}/debug-toc`. This requirement adds a specific spec-level check on top of the generic "all routes documented" rule, because the register form and the absence of the debug-toc endpoint are both behavioural changes from the previous active spec.

#### Scenario: Register example omits tocPage
- **WHEN** the `.http` file is opened
- **THEN** the `POST /admin/books/register` block contains a multipart body with `file`, `sourceName`, `version`, and `displayName` parts and does NOT contain a `tocPage` part

#### Scenario: No debug-toc example exists
- **WHEN** the `.http` file is searched for `debug-toc`
- **THEN** no match is found
