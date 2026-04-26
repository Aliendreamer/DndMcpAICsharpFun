# Roslyn Warnings — Full Fix & Build Gate

**Date:** 2026-04-26
**Status:** Completed

## Goal

Surface every Roslyn analyzer and style warning in `dotnet build`, fix all of them, and keep the build permanently clean with `TreatWarningsAsErrors=true`.

## Scope

- `DndMcpAICsharpFun` (single project, no test projects yet)
- All `.cs` files except generated `Migrations/`
- No suppressions — every rule either gets fixed or explicitly demoted in `.editorconfig`

---

## Section 1: Build Gate

Add to `DndMcpAICsharpFun.csproj`:

```xml
<EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
```

`EnforceCodeStyleInBuild` causes IDE/style rules from `.editorconfig` to surface during `dotnet build`. `TreatWarningsAsErrors` makes any warning a hard build failure — nothing merges with a warning.

---

## Section 2: CA1873 — LoggerMessage Source Generators

**Rule:** CA1873 — "Evaluation of this argument may be expensive and unnecessary if logging is disabled."

**Fix:** Replace all direct `_logger.LogXxx(...)` calls with `[LoggerMessage]`-generated methods. The source generator emits code that checks `IsEnabled` before evaluating any arguments, eliminating unnecessary allocations when a log level is disabled.

### Pattern

`[LoggerMessage]` partial methods are declared **directly on the outer `partial` class** (not in a nested class). The outer class must be `partial` because the source generator emits a sibling partial declaration for it.

```csharp
// Before
_logger.LogInformation("Starting ingestion for {DisplayName} (id={Id})", record.DisplayName, recordId);

// After — method declared directly on the outer partial class
LogStartingIngestion(logger, record.DisplayName, recordId);

[LoggerMessage(Level = LogLevel.Information, Message = "Starting ingestion for {DisplayName} (id={Id})")]
private static partial void LogStartingIngestion(ILogger logger, string displayName, int id);
```

Naming convention: `Log` prefix + PascalCase description (e.g. `LogRecordNotFound`, `LogCycleStarting`).

> **Why not a nested `Log` class?** Nesting `[LoggerMessage]` methods inside a `private static partial class Log` causes CS8795 in IDEs that don't run source generators during background analysis. Placing the methods directly on the outer class avoids this while keeping the build clean.

### Files

| File | Log calls |
|------|-----------|
| `Features/Ingestion/IngestionOrchestrator.cs` | 5 |
| `Features/Ingestion/IngestionBackgroundService.cs` | 4 |
| `Features/Embedding/EmbeddingIngestor.cs` | 2 |
| `Infrastructure/Qdrant/QdrantCollectionInitializer.cs` | 4 |
| `Features/Ingestion/Pdf/PdfPigTextExtractor.cs` | 1 |
| `Features/Admin/BooksAdminEndpoints.cs` | 1 |

**Total:** 17 call sites across 6 files.

---

## Section 3: Style Rule Cleanup

Additional changes applied across the codebase to satisfy `.editorconfig` style rules:

| Category | Rule | Fix applied |
|----------|------|-------------|
| Primary constructors | `csharp_style_prefer_primary_constructors` | Collapsed field-assignment constructors to primary constructor form in all service classes |
| Collection expressions | `dotnet_style_prefer_collection_expression` | `new string[] { ... }` → `[...]` in `QdrantCollectionInitializer` |
| Static lambdas | `csharp_prefer_static_anonymous_function` | Added `static` to non-capturing lambdas in `Program.cs` and `RagRetrievalService.cs` |
| Readonly fields | `dotnet_style_readonly_field` | Derived fields kept as explicit `private readonly` where needed (e.g. `_batchSize`, `_options`) |

**Done criterion:** `dotnet build` exits with 0 errors, 0 warnings. ✅

---

## Security Fix

While implementing `BooksAdminEndpoints`, a path traversal vulnerability was identified and fixed:

```csharp
// Before — attacker-controlled filename could contain ../sequences
var filePath = Path.Combine(booksPath, file.FileName);

// After — Path.GetFileName strips any directory components
var filePath = Path.Combine(booksPath, Path.GetFileName(file.FileName));
```

---

## Migrations Exclusion

EF Core migration files are generated code and must be excluded from the enforced ruleset. Added to `.editorconfig`:

```ini
[Migrations/**]
generated_code = true
dotnet_analyzer_diagnostic.severity = none
```

---

## Out of Scope

- Migrations (`Migrations/`) — excluded via `.editorconfig` as described above
- Adding new analyzer packages (e.g., SonarAnalyzer, Roslynator) — separate initiative
- Test project coverage — no tests exist yet
