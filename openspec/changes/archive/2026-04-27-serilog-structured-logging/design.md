## Context

The application uses ASP.NET Core's default `ILoggerFactory` with only the console provider, configured via `builder.Logging`. There is no file sink, no structured output format, and no log retention. Debugging the background ingestion pipeline — which processes books page-by-page via Ollama and writes vectors to Qdrant — requires reading live console output and has no durable history.

Serilog integrates as a drop-in replacement for the default provider: all existing `ILogger<T>` injection and `[LoggerMessage]` source-gen methods continue to work unchanged; Serilog handles routing to sinks.

## Goals / Non-Goals

**Goals:**
- Replace default logging provider with Serilog, configuration-driven via `appsettings`
- Persist logs to daily-rolling files with CLEF (Compact Log Event Format) structured JSON, 7-day retention
- Log every HTTP request/response via `UseSerilogRequestLogging()` middleware
- Add structured `[LoggerMessage]` log points across all pipeline components with timing, page numbers, model names, entity counts
- Clean up dev config: remove overrides for libraries not in the project, add `DndMcpAICsharpFun: Debug`

**Non-Goals:**
- Centralised log aggregation (Seq, Elastic, Loki) — out of scope; CLEF file format makes future adoption trivial
- Log-based alerting or dashboards
- Changing any public API or altering behavior; logging is purely additive

## Decisions

**Decision 1: `UseSerilog(ReadFrom.Configuration)` over programmatic setup**

Rationale: The user has already defined the full Serilog config in `appsettings.Development.json`. `ReadFrom.Configuration()` maps that JSON directly to sinks, levels, and enrichers with no code duplication. Changing log levels or sinks becomes a config-only change with no recompile needed.

Alternative considered: Programmatic `LoggerConfiguration` in `Program.cs`. Rejected because it duplicates what the config already expresses and makes environment-specific tuning harder.

**Decision 2: `[LoggerMessage]` source-gen for all new log points**

Rationale: Already the established pattern in the codebase (`IngestionQueueWorker`, `BooksAdminEndpoints`). Source-gen avoids boxing allocations and string interpolation on hot paths (per-page LLM loops process hundreds of calls per book). Consistency matters more than the marginal convenience of `_logger.LogInformation(...)`.

Alternative considered: Regular `_logger.LogXxx()` calls for one-off messages. Rejected to keep a single logging style throughout.

**Decision 3: `Stopwatch` per operation for elapsed timing**

Each operation (ingest, extract, embed) wraps its work in a `Stopwatch`. The elapsed milliseconds are passed as a structured property to the completion log message. This gives queryable timing data in the CLEF file without any external instrumentation.

**Decision 4: Debug level for per-page and Ollama detail logs**

Per-page progress ("Classifying page 47/200") and Ollama timing are `Debug`. The `DndMcpAICsharpFun: Debug` override in dev config makes them visible locally. In production (where `appsettings.json` sets `Default: Information`), they are suppressed automatically — no code change needed.

## Risks / Trade-offs

**Buffered file sink** → If the process crashes, up to one buffer's worth of recent log events may be lost. Mitigation: acceptable for development; production would use `buffered: false` or a more durable sink. The current config sets `buffered: true` which is the user's explicit choice.

**Per-page Debug logs** → At Debug level in dev, a 500-page book generates ~1000 log entries during extraction. File sink handles this volume trivially; console output can be noisy. Mitigation: `DndMcpAICsharpFun` override can be raised to `Information` if console becomes unreadable.

**`[LoggerMessage]` ceremony** → Requires `partial class` on the enclosing type. `IngestionOrchestrator` and `OllamaLlmClassifier` etc. must be made `partial` if they aren't already. This is a purely mechanical, non-breaking change.

## Migration Plan

1. Add NuGet packages (no app behavior changes yet)
2. Wire `UseSerilog()` in `Program.cs` — default provider replaced; existing log output continues via Serilog
3. Update `appsettings.json` and `appsettings.Development.json` — log config takes effect
4. Add `[LoggerMessage]` methods to each component — purely additive
5. No rollback needed; reverting is removing `UseSerilog()` and the packages

## Open Questions

None — all decisions resolved during brainstorming.
