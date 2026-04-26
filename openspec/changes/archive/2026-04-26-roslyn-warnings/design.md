## Build Gate

`EnforceCodeStyleInBuild=true` surfaces `.editorconfig` IDE/style rules during `dotnet build` (not just in the IDE). `TreatWarningsAsErrors=true` makes any warning a hard build failure.

EF Core migration files are generated code — excluded via:

```ini
[Migrations/**]
generated_code = true
dotnet_analyzer_diagnostic.severity = none
```

## [LoggerMessage] Pattern

`[LoggerMessage]` partial methods are declared directly on the outer `partial` class — not in a nested class:

```csharp
// outer class must be partial (source generator emits a sibling partial declaration)
public sealed partial class MyService(ILogger<MyService> logger)
{
    private void DoWork()
    {
        LogStarting(logger);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Starting work")]
    private static partial void LogStarting(ILogger logger);
}
```

Naming: `Log` prefix + PascalCase description. The source generator emits an `IsEnabled`-gated implementation — no allocation when the log level is disabled.

**Why not a nested `Log` class?** A `private static partial class Log` containing `[LoggerMessage]` methods causes CS8795 in IDEs that run Roslyn analysis without source generators. Placing declarations directly on the outer class avoids this.

## Path Traversal Fix

`file.FileName` from an `IFormFile` is attacker-controlled. `Path.Combine(booksPath, file.FileName)` allows directory traversal if the filename contains `../` sequences. Fix: `Path.Combine(booksPath, Path.GetFileName(file.FileName))` — `Path.GetFileName` strips all directory components.

## Primary Constructors

Classes with field-assignment constructors were converted to primary constructor form (C# 12). Derived fields that cannot be inlined (e.g. `options.Value.EmbeddingBatchSize`) remain as explicit `private readonly` fields initialised from the primary constructor parameter.
