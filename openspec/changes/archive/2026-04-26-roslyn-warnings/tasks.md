## 1. Build Gate

- [x] 1.1 Add `EnforceCodeStyleInBuild=true` and `TreatWarningsAsErrors=true` to `DndMcpAICsharpFun.csproj`
- [x] 1.2 Add `[Migrations/**]` exclusion block to `.editorconfig`
- [x] 1.3 `dotnet build` passes with 0 errors, 0 warnings

## 2. LoggerMessage — IngestionOrchestrator

- [x] 2.1 Declare class `partial`
- [x] 2.2 Replace 5 direct `_logger` calls with `[LoggerMessage]` partial methods on the outer class
- [x] 2.3 `dotnet build` passes

## 3. LoggerMessage — IngestionBackgroundService

- [x] 3.1 Declare class `partial`; convert to primary constructor
- [x] 3.2 Replace 4 direct `_logger` calls with `[LoggerMessage]` partial methods
- [x] 3.3 `dotnet build` passes

## 4. LoggerMessage — EmbeddingIngestor

- [x] 4.1 Declare class `partial`; convert to primary constructor
- [x] 4.2 Replace 2 direct `_logger` calls with `[LoggerMessage]` partial methods
- [x] 4.3 Add `static` to `.Select(static c => c.Text)` lambda
- [x] 4.4 `dotnet build` passes

## 5. LoggerMessage — QdrantCollectionInitializer

- [x] 5.1 Declare class `partial`; convert to primary constructor
- [x] 5.2 Replace 4 direct `_logger` calls with `[LoggerMessage]` partial methods
- [x] 5.3 Replace `new string[] { ... }` with collection expressions `[...]`
- [x] 5.4 `dotnet build` passes

## 6. RagRetrievalService — primary constructor + static lambdas

- [x] 6.1 Convert to primary constructor
- [x] 6.2 Add `static` to both `.Select(static p => ...)` lambdas
- [x] 6.3 `dotnet build` passes

## 7. LoggerMessage — PdfPigTextExtractor

- [x] 7.1 Declare class `partial`
- [x] 7.2 Replace 1 direct `_logger` call with `[LoggerMessage]` partial method
- [x] 7.3 `dotnet build` passes

## 8. LoggerMessage + Security — BooksAdminEndpoints

- [x] 8.1 Declare class `partial`
- [x] 8.2 Replace 1 direct `_logger` call with `[LoggerMessage]` partial method
- [x] 8.3 Fix path traversal: `Path.Combine(booksPath, Path.GetFileName(file.FileName))`
- [x] 8.4 `dotnet build` passes

## 9. Program.cs — static lambdas

- [x] 9.1 Add `static` to `QdrantClient`, `OllamaApiClient`, `IngestionDbContext` factory lambdas
- [x] 9.2 Add `static` to `UseWhen` predicates
- [x] 9.3 `dotnet build` passes

## 10. Final verification

- [x] 10.1 `dotnet clean && dotnet build` exits with 0 errors, 0 warnings
