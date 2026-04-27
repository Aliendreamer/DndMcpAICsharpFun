## Why

The application currently uses the default ASP.NET Core logging provider, which produces unstructured console output with no file persistence, rotation, or retention. When something goes wrong in the background ingestion pipeline — a failed LLM call, a stuck queue item, an unexpected exception — there is no durable record and no way to correlate events across the orchestrator, Ollama clients, and vector store. Serilog provides structured, queryable log output to both console and rotating daily files with 7-day retention, making the system observable without any external tooling.

## What Changes

- Add Serilog as the sole logging backend, wired via `UseSerilog(ReadFrom.Configuration)` so `appsettings` drives all sink and level configuration
- Add `UseSerilogRequestLogging()` middleware for one structured log line per HTTP request (method, path, status, elapsed ms)
- Add NuGet packages: `Serilog.AspNetCore`, `Serilog.Formatting.Compact`, `Serilog.Enrichers.Environment`, `Serilog.Enrichers.Thread`
- Add base Serilog config to `appsettings.json` (Console sink, Information level) for non-Development environments
- Clean up `appsettings.Development.json`: remove overrides for unused libraries (Hangfire, Redis, CAP, Quartz, RabbitMQ, FastEndpoints), add `DndMcpAICsharpFun: Debug` override
- Add `[LoggerMessage]`-based structured log points across all pipeline components:
  - `IngestionQueueWorker`: work item started/completed with elapsed
  - `IngestionOrchestrator`: start/complete/fail for ingest, extract, ingest-json, delete; per-page progress during LLM extraction
  - `OllamaLlmClassifier`: per-page classify start/done with category and elapsed
  - `OllamaLlmEntityExtractor`: per-page extract start/done with entity count and elapsed
  - `OllamaEmbeddingService`: embed batch start/done with chunk count and elapsed
  - `EmbeddingIngestor`: ingestor start/done
  - `QdrantVectorStoreService`: upsert start/done with vector count

## Capabilities

### New Capabilities

- `structured-logging`: Serilog backend wiring, configuration, HTTP request logging middleware, and all `[LoggerMessage]` log points across the ingestion pipeline and supporting services

### Modified Capabilities

- `ingestion-pipeline`: Per-page progress logging added inside the LLM extraction loop; failure state transitions now have corresponding error log events
- `infrastructure-clients`: Ollama and Qdrant client operations gain Debug-level timing log points

## Impact

- **Program.cs**: `UseSerilog()` on host builder, `UseSerilogRequestLogging()` on app pipeline
- **DndMcpAICsharpFun.csproj**: four new NuGet package references
- **Config/appsettings.json**: new `Serilog` section (base config)
- **Config/appsettings.Development.json**: cleaned overrides, added `DndMcpAICsharpFun: Debug`
- **Features/Ingestion/IngestionQueueWorker.cs**: two new `[LoggerMessage]` methods
- **Features/Ingestion/IngestionOrchestrator.cs**: ~10 new `[LoggerMessage]` methods
- **Features/Ingestion/Extraction/OllamaLlmClassifier.cs**: two new `[LoggerMessage]` methods
- **Features/Ingestion/Extraction/OllamaLlmEntityExtractor.cs**: two new `[LoggerMessage]` methods
- **Features/Embedding/OllamaEmbeddingService.cs**: two new `[LoggerMessage]` methods
- **Features/Embedding/EmbeddingIngestor.cs**: two new `[LoggerMessage]` methods
- **Features/VectorStore/QdrantVectorStoreService.cs**: two new `[LoggerMessage]` methods
- No API contract changes; no breaking changes
