# Roslyn Warnings — Full Fix & Build Gate

**Date:** 2026-04-26
**Status:** Approved

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

Each logging class gets a `private static partial class Log` nested inside it:

```csharp
// Before
_logger.LogInformation("Starting ingestion for {DisplayName} (id={Id})", record.DisplayName, recordId);

// After
Log.StartingIngestion(_logger, record.DisplayName, recordId);

private static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Starting ingestion for {DisplayName} (id={Id})")]
    public static partial void StartingIngestion(ILogger logger, string displayName, int id);
}
```

### Files

| File | Log calls |
|------|-----------|
| `Features/Ingestion/IngestionOrchestrator.cs` | 6 |
| `Features/Ingestion/IngestionBackgroundService.cs` | 4 |
| `Features/Embedding/EmbeddingIngestor.cs` | 2 |
| `Infrastructure/Qdrant/QdrantCollectionInitializer.cs` | 4 |
| `Features/Ingestion/Pdf/PdfPigTextExtractor.cs` | 1 |
| `Features/Admin/BooksAdminEndpoints.cs` | 1 |

**Total:** ~18 call sites across 6 files.

---

## Section 3: Style Rule Cleanup

After Sections 1 and 2, run `dotnet build` to discover remaining violations. Fix them in batches, rebuilding after each to confirm the count drops. Expected categories (based on `.editorconfig` `suggestion`-severity rules):

| Category | Rule(s) | Typical fix |
|----------|---------|-------------|
| Primary constructors | `csharp_style_prefer_primary_constructors` | Collapse field-assignment constructors to primary constructor form |
| Pattern matching | `csharp_style_pattern_matching_over_is_with_cast_check`, `csharp_style_prefer_not_pattern` | Replace `is`/`as` casts with pattern syntax |
| Expression-bodied members | `csharp_style_expression_bodied_accessors`, `_properties` | Collapse single-expression getters/properties |
| Collection expressions | `dotnet_style_prefer_collection_expression` | `new List<T> { }` → `[...]` |
| Readonly fields | `dotnet_style_readonly_field` | Add `readonly` to fields not mutated after construction |
| Static local functions | `csharp_prefer_static_local_function` | Add `static` to local functions that capture no state |

**Done criterion:** `dotnet build` exits with 0 errors, 0 warnings.

---

## Migrations Exclusion

EF Core migration files are generated code and must be excluded from the enforced ruleset. Add a `.editorconfig` override at the top of the file (or in a `Migrations/.editorconfig`):

```ini
[Migrations/**/*.cs]
generated_code = true
dotnet_analyzer_diagnostic.severity = none
```

This suppresses all analyzer diagnostics for migration files without touching the rest of the ruleset.

---

## Out of Scope

- Migrations (`Migrations/`) — excluded via `.editorconfig` as described above
- Adding new analyzer packages (e.g., SonarAnalyzer, Roslynator) — separate initiative
- Test project coverage — no tests exist yet
