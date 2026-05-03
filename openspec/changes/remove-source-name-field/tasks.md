# Implementation Tasks

## 1. Domain & persistence

- [ ] 1.1 Remove `SourceName` property from `Infrastructure/Sqlite/IngestionRecord.cs` (also drop its `[Required, MaxLength(100)]` attributes).
- [ ] 1.2 Generate EF migration `dotnet ef migrations add RemoveSourceNameFromIngestionRecord` — `Up` drops the column, `Down` re-adds it as nullable.

## 2. Register endpoint

- [ ] 2.1 Remove the `case "sourceName": sourceName = value; break;` branch from `BooksAdminEndpoints.RegisterBook`.
- [ ] 2.2 Remove the `string? sourceName` declaration from the local-variable list.
- [ ] 2.3 Remove the `if (string.IsNullOrEmpty(sourceName) || ...)` half of the validation guard — only `displayName` matters.
- [ ] 2.4 Remove `SourceName = sourceName` from the `new IngestionRecord { ... }` initializer.

## 3. Documentation

- [ ] 3.1 Remove the `sourceName` form part from the register block in `DndMcpAICsharpFun.http`.

## 4. Tests

- [ ] 4.1 Update `BooksAdminEndpointsTests.cs` — drop every `content.Add(new StringContent("..."), "sourceName")` line and any `Arg.Is<IngestionRecord>(r => r.SourceName == ...)` assertions. Adjust `MakeRecord` helper to drop `SourceName`.
- [ ] 4.2 Update any other test that constructs `IngestionRecord { SourceName = ... }` (search the test project for `SourceName`).
- [ ] 4.3 Update `SqliteIngestionTrackerTests` fixture if it sets `SourceName` on the sample record.

## 5. Verification

- [ ] 5.1 `dotnet build` — zero errors.
- [ ] 5.2 `dotnet test` — all tests pass.
- [ ] 5.3 Manual smoke: register a book without sending `sourceName`, confirm HTTP 202 and the SQLite row is created. Confirm `GET /admin/books` shows the record without a `sourceName` field.
- [ ] 5.4 `openspec status --change remove-source-name-field` shows all four artifacts done.
