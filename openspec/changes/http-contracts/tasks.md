## 1. Create DndMcpAICsharpFun.http

- [x] 1.1 Create `DndMcpAICsharpFun.http` at the project root with `@baseUrl` and `@adminKey` variable declarations
- [x] 1.2 Add Health section — `GET /health`, `GET /ready`, `GET /health/ready`
- [x] 1.3 Add Admin — Books section — `POST /admin/books/register` (multipart), `GET /admin/books`, `POST /admin/books/{id}/reingest`; all with `X-Api-Key: {{adminKey}}`
- [x] 1.4 Add Retrieval section — `GET /retrieval/search` and `GET /admin/retrieval/search` with representative query params (`q`, `version`, `category`, `topK`); admin variant includes `X-Api-Key: {{adminKey}}`
- [x] 1.5 Add Metrics section — `GET /metrics`

## 2. Update CLAUDE.md

- [x] 2.1 Add an "API Contracts" section to `CLAUDE.md` with the rule: when adding, changing, or removing any route (`MapGet`, `MapPost`, `MapPut`, `MapDelete`), update `DndMcpAICsharpFun.http` in the same commit

## 3. Verify and Commit

- [x] 3.1 Open `DndMcpAICsharpFun.http` and confirm all 8 routes are present with correct paths, methods, headers, and example bodies
- [x] 3.2 Confirm `dotnet build` passes — 0 errors, 0 warnings
- [x] 3.3 Commit: `feat: add DndMcpAICsharpFun.http with all endpoints and API Contracts rule`
