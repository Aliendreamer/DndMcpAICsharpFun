# structured-logging Specification

## Purpose
TBD - created by archiving change serilog-structured-logging. Update Purpose after archive.
## Requirements
### Requirement: Serilog backend wiring
The system SHALL use Serilog as the sole logging provider, configured via `UseSerilog((ctx, cfg) => cfg.ReadFrom.Configuration(ctx.Configuration))` on the host builder. All existing `ILogger<T>` injection points SHALL continue to function without modification.

#### Scenario: App starts with Serilog
- **WHEN** the application starts
- **THEN** log output is routed through Serilog sinks (console and file) instead of the default provider

### Requirement: HTTP request logging middleware
The system SHALL log one structured log entry per HTTP request via `app.UseSerilogRequestLogging()`, including request method, path, response status code, and elapsed milliseconds as structured properties.

#### Scenario: Admin endpoint called
- **WHEN** a request is made to any admin endpoint
- **THEN** a single structured log line is emitted at Information level containing method, path, status code, and elapsed ms

### Requirement: Daily rotating file sink
The system SHALL write logs to a daily-rolling file at `logs/service.log` using CLEF (Compact Log Event Format), retaining a maximum of 7 files, with a 1 GB per-file size limit.

#### Scenario: New day begins
- **WHEN** the date changes
- **THEN** a new log file is created and the oldest file beyond the 7-file limit is removed

### Requirement: Log enrichment
All log events SHALL be enriched with `MachineName` and `ThreadId` as structured properties via `FromLogContext`, `WithMachineName`, and `WithThreadId` enrichers.

#### Scenario: Log event emitted
- **WHEN** any log event is written
- **THEN** the event contains MachineName and ThreadId properties

### Requirement: DndMcpAICsharpFun namespace at Debug in development
In the Development environment, the `DndMcpAICsharpFun` namespace SHALL log at `Debug` level so per-page progress and Ollama timing detail are visible. All other third-party namespaces SHALL log at `Warning` or above.

#### Scenario: Per-page debug log in development
- **WHEN** the application runs in Development and processes a book page
- **THEN** Debug-level log messages from the `DndMcpAICsharpFun` namespace appear in the output

### Requirement: IngestionQueueWorker work item lifecycle logging
`IngestionQueueWorker` SHALL emit a structured log at Information level when a work item starts processing and when it completes, including the work type, book ID, and elapsed milliseconds on completion.

#### Scenario: Extract work item processed
- **WHEN** a work item of type Extract is dequeued and processed
- **THEN** a "started" log is emitted before processing and a "completed" log with elapsed ms is emitted after

### Requirement: IngestionOrchestrator operation lifecycle logging
`IngestionOrchestrator` SHALL emit structured logs for each pipeline operation (Ingest, Extract, IngestJson, Delete):
- Information on start with book ID and relevant metadata (file path, file count)
- Information on completion with result metrics (chunk count, page count, entity count) and elapsed ms
- Error on failure with the exception

#### Scenario: Extract operation fails
- **WHEN** `ExtractBookAsync` throws an unhandled exception
- **THEN** an Error log is emitted with the book ID and exception details

### Requirement: Per-page extraction progress logging
During LLM extraction, `IngestionOrchestrator` SHALL emit a Debug log before classifying each page and after completing extraction for each page, including page number and total page count.

#### Scenario: Page classification starts
- **WHEN** the orchestrator begins classifying page 47 of 200
- **THEN** a Debug log is emitted: "Classifying page 47/200 for book {BookId}"

#### Scenario: Page extraction completes
- **WHEN** extraction for page 47 completes
- **THEN** a Debug log is emitted: "Extracted page 47/200 for book {BookId}"

### Requirement: Ollama client operation logging
`OllamaLlmClassifier` and `OllamaLlmEntityExtractor` SHALL emit Debug logs on start and completion of each LLM call, including model name, page number, and on completion: category (classifier) or entity count (extractor), and elapsed ms.

#### Scenario: Classifier call completes
- **WHEN** `OllamaLlmClassifier` receives a response for page 5
- **THEN** a Debug log is emitted with model name, page number, classified category, and elapsed ms

### Requirement: Embedding and vector store operation logging
`OllamaEmbeddingService`, `EmbeddingIngestor`, and `QdrantVectorStoreService` SHALL emit Debug logs on start and completion of batch operations, including chunk/vector counts and elapsed ms on completion.

#### Scenario: Embedding batch completes
- **WHEN** `OllamaEmbeddingService` finishes embedding a batch of chunks
- **THEN** a Debug log is emitted with model name, chunk count, and elapsed ms

