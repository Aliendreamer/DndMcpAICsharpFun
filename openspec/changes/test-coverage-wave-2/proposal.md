## Why

The project currently sits at 62.7% line coverage. Four production classes — `PdfPigTextExtractor`, `OllamaEmbeddingService`, `RetrievalEndpoints`, and `OllamaHealthCheck` — are at 0%, and several Qdrant/DI-wiring classes are untestable without external infrastructure and should be excluded from coverage reporting.

## What Changes

- Fix `OllamaEmbeddingService` and `OllamaHealthCheck` to depend on `IOllamaApiClient` (interface) instead of the concrete `OllamaApiClient`, making them mockable
- Add unit tests for `PdfPigTextExtractor` using `PdfDocumentBuilder` in-memory PDFs
- Add unit tests for `OllamaEmbeddingService` (success path + `HttpRequestException` wrapping)
- Add unit tests for `OllamaHealthCheck` (healthy + unhealthy paths)
- Add HTTP integration tests for `RetrievalEndpoints` (public search + admin diagnostic search)
- Add `[ExcludeFromCodeCoverage]` to Qdrant infrastructure classes and DI-wiring classes that are intentionally untested
- Configure coverlet `ExcludeByFile` for `Program.cs`

## Capabilities

### New Capabilities

- `pdf-text-extractor-tests`: Unit tests for `PdfPigTextExtractor` using in-memory PDFs
- `embedding-service-tests`: Unit tests for `OllamaEmbeddingService` with mocked Ollama client
- `retrieval-endpoints-tests`: HTTP integration tests for `RetrievalEndpoints`
- `ollama-health-check-tests`: Unit tests for `OllamaHealthCheck` with mocked Ollama client
- `coverage-exclusions`: `[ExcludeFromCodeCoverage]` on untestable infrastructure classes

### Modified Capabilities

- `ollama-client-abstraction`: `OllamaEmbeddingService` and `OllamaHealthCheck` now depend on `IOllamaApiClient` instead of the concrete class

## Impact

- `Features/Embedding/OllamaEmbeddingService.cs` — constructor parameter type change
- `Infrastructure/Ollama/OllamaHealthCheck.cs` — constructor parameter type change
- `Infrastructure/Qdrant/` classes — attribute added, no behavior change
- `Extensions/ServiceCollectionExtensions.cs`, `Extensions/WebApplicationExtensions.cs` — attribute added
- DI registration in `Program.cs` / `ServiceCollectionExtensions` unchanged (concrete `OllamaApiClient` implements `IOllamaApiClient`)
- Test project: new test files under `DndMcpAICsharpFun.Tests/`
