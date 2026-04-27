## 1. Packages and Bootstrap

- [x] 1.1 Add NuGet packages to `DndMcpAICsharpFun.csproj`: `Serilog.AspNetCore`, `Serilog.Formatting.Compact`, `Serilog.Enrichers.Environment`, `Serilog.Enrichers.Thread` (run `dotnet add package` for each)
- [x] 1.2 Wire Serilog in `Program.cs`: add `builder.Host.UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration));` before `builder.Build()`, and add `app.UseSerilogRequestLogging();` before the admin middleware and route mappings
- [x] 1.3 Add `using Serilog;` to `Program.cs`

## 2. Configuration Cleanup

- [x] 2.1 Update `Config/appsettings.json` Serilog section: remove `Serilog.Expressions` from `Using` array, change `Default` level from `"Debug"` to `"Information"`, remove overrides for `FastEndpoints`, `Quartz`, `Microsoft.AspNetCore.HttpOverrides.ForwardedHeadersMiddleware` — keep only `Microsoft: Warning`, `Microsoft.AspNetCore: Warning`, `System.Net.Http.HttpClient: Warning`, `Microsoft.EntityFrameworkCore.Database.Command: Warning`
- [x] 2.2 Update `Config/appsettings.Development.json` Serilog overrides: remove `FastEndpoints`, `OpenTelemetry`, `Quartz`, `Hangfire`, `Hangfire.Server`, `Hangfire.SqlServer`, `StackExchange.Redis`, `Microsoft.AspNetCore.HttpOverrides.ForwardedHeadersMiddleware`, `DotNetCore.CAP`, `DotNetCore.CAP.RabbitMQ`, `RabbitMQ.Client` — add `"DndMcpAICsharpFun": "Debug"` — keep `Microsoft: Warning`, `Microsoft.AspNetCore: Warning`, `Microsoft.EntityFrameworkCore.Database.Command: Debug`, `System.Net.Http.HttpClient: Information`

## 3. IngestionQueueWorker Logging

- [x] 3.1 In `Features/Ingestion/IngestionQueueWorker.cs`, add a `Stopwatch` around the `await (item.Type switch {...})` call: start before the switch, stop after. Add two `[LoggerMessage]` methods at the bottom of the class: `LogWorkItemStarted(ILogger, IngestionWorkType, int)` at Information ("Starting {Type} for book {BookId}") and `LogWorkItemCompleted(ILogger, IngestionWorkType, int, long)` at Information ("Completed {Type} for book {BookId} in {ElapsedMs}ms"). Call `LogWorkItemStarted` before the switch and `LogWorkItemCompleted` after with `stopwatch.ElapsedMilliseconds`

## 4. IngestionOrchestrator Per-Page Logging

- [x] 4.1 In `Features/Ingestion/IngestionOrchestrator.cs`, add two `[LoggerMessage]` methods: `LogClassifyingPage(ILogger, int, int, int)` at Debug ("Classifying page {Page}/{Total} for book {BookId}") and `LogExtractedPage(ILogger, int, int, int)` at Debug ("Extracted page {Page}/{Total} for book {BookId}")
- [x] 4.2 In `ExtractBookAsync`, call `LogClassifyingPage` at the start of the `foreach (var (pageNumber, pageText) in pages)` loop body (before `classifier.ClassifyPageAsync`) and `LogExtractedPage` after `jsonStore.SavePageAsync` (at the end of the loop body). Pass `pages.Count` as `total`

## 5. OllamaLlmClassifier Logging

- [x] 5.1 In `Features/Ingestion/Extraction/OllamaLlmClassifier.cs`, add a `Stopwatch` inside `ClassifyPageAsync`. Add two `[LoggerMessage]` methods: `LogClassifyStart(ILogger, int)` at Debug ("Classifying page {Page}") and `LogClassifyDone(ILogger, int, string, long)` at Debug ("Classified page {Page} as {Category} in {ElapsedMs}ms"). Call start before the LLM request, call done after with the first returned category (or "none") and elapsed ms

## 6. OllamaLlmEntityExtractor Logging

- [x] 6.1 In `Features/Ingestion/Extraction/OllamaLlmEntityExtractor.cs`, add a `Stopwatch` inside `ExtractAsync`. Add two `[LoggerMessage]` methods: `LogExtractStart(ILogger, int, string)` at Debug ("Extracting {EntityType} from page {Page}") and `LogExtractDone(ILogger, int, string, int, long)` at Debug ("Extracted {Count} {EntityType} from page {Page} in {ElapsedMs}ms"). Call start before the LLM request, call done after parsing with entity count and elapsed ms

## 7. OllamaEmbeddingService Logging

- [x] 7.1 In `Features/Embedding/OllamaEmbeddingService.cs`, change the class declaration from `public sealed class` to `public sealed partial class` and add `ILogger<OllamaEmbeddingService> logger` to the primary constructor parameters
- [x] 7.2 Add two `[LoggerMessage]` methods to `OllamaEmbeddingService`: `LogEmbedBatchStart(ILogger, int, string)` at Debug ("Embedding {ChunkCount} chunks with model {Model}") and `LogEmbedBatchDone(ILogger, int, string, long)` at Debug ("Embedded {ChunkCount} chunks with {Model} in {ElapsedMs}ms"). Wrap the embedding call with a `Stopwatch` and call both log methods

## 8. EmbeddingIngestor Logging

- [x] 8.1 In `Features/Embedding/EmbeddingIngestor.cs`, add two `[LoggerMessage]` methods: `LogIngestStart(ILogger, int)` at Information ("Starting embedding ingestion for book {BookId}") and `LogIngestComplete(ILogger, int, long)` at Information ("Embedding ingestion complete for book {BookId} in {ElapsedMs}ms"). Wrap the `IngestAsync` body with a `Stopwatch` and call both methods

## 9. QdrantVectorStoreService Logging

- [x] 9.1 In `Features/VectorStore/QdrantVectorStoreService.cs`, change the class declaration from `public sealed class` to `public sealed partial class` and add `ILogger<QdrantVectorStoreService> logger` to the primary constructor parameters
- [x] 9.2 Add two `[LoggerMessage]` methods to `QdrantVectorStoreService`: `LogUpsertStart(ILogger, int, string)` at Debug ("Upserting {Count} vectors into collection {Collection}") and `LogUpsertDone(ILogger, int, string, long)` at Debug ("Upserted {Count} vectors into {Collection} in {ElapsedMs}ms"). Wrap the upsert call with a `Stopwatch` and call both log methods

## 10. Build and Verify

- [x] 10.1 Run `dotnet build` and confirm zero errors and zero warnings
- [x] 10.2 Run `dotnet test` and confirm all existing tests pass
