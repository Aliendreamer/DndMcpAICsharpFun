## 1. Remove Background Service

- [ ] 1.1 Delete `Features/Ingestion/IngestionBackgroundService.cs`
- [ ] 1.2 Remove `services.AddHostedService<IngestionBackgroundService>()` from `Extensions/ServiceCollectionExtensions.cs`
- [ ] 1.3 Run `dotnet build` and confirm it compiles with no errors

## 2. Update Register Handlers

- [ ] 2.1 In `Features/Admin/BooksAdminEndpoints.cs` — `RegisterBook` handler: remove the `IServiceScopeFactory scopeFactory` parameter and the entire `Task.Run` fire-and-forget block; keep all other logic unchanged
- [ ] 2.2 In `Features/Admin/BooksAdminEndpoints.cs` — `RegisterBookByPath` handler: remove the `IServiceScopeFactory scopeFactory` parameter and the entire `Task.Run` fire-and-forget block; keep all other logic unchanged
- [ ] 2.3 Run `dotnet build` and confirm it compiles with no errors

## 3. Remove Background Service Tests

- [ ] 3.1 Delete `DndMcpAICsharpFun.Tests/Ingestion/IngestionBackgroundServiceTests.cs`
- [ ] 3.2 Run `dotnet test` and confirm all remaining tests pass

## 4. Update API Contracts

- [ ] 4.1 In `DndMcpAICsharpFun.http` — verify register examples document that the book remains `Pending` after registration (add a comment if not already clear)
- [ ] 4.2 In `README.md` — update the register endpoint description to note that registration does not start ingestion; operators must call `/reingest`, `/extract`, or `/ingest-json` explicitly

## 5. Commit

- [ ] 5.1 Stage and commit all changes: `git add -A && git commit -m "feat: remove background ingestion — registration stays Pending, all pipeline execution is explicit"`
