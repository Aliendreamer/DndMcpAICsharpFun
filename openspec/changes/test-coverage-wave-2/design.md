## Context

The project has six classes at 0% coverage: `PdfPigTextExtractor`, `OllamaEmbeddingService`, `OllamaHealthCheck`, `RetrievalEndpoints`, plus Qdrant infrastructure and DI-wiring code. The previous session (test-coverage-wave-1) brought overall coverage from 41.5% to 62.7%. This wave targets the remaining testable classes.

`OllamaEmbeddingService` and `OllamaHealthCheck` currently take `OllamaApiClient` (concrete class) rather than `IOllamaApiClient`. The classifier was already fixed in wave-1; these two were missed. The concrete type cannot be mocked with NSubstitute.

## Goals / Non-Goals

**Goals:**
- Reach ~75%+ line coverage
- Test `PdfPigTextExtractor`, `OllamaEmbeddingService`, `OllamaHealthCheck`, and `RetrievalEndpoints`
- Fix `IOllamaApiClient` abstraction on embedding and health check
- Exclude Qdrant and DI-wiring code from coverage reporting (intentionally untestable without infrastructure)

**Non-Goals:**
- Testing Qdrant vector store functionality (requires Testcontainers / external service)
- Testing `ServiceCollectionExtensions` or `Program.cs` wiring end-to-end
- Testing `WebApplicationExtensions` DB migration code

## Decisions

### D1: `IOllamaApiClient` over concrete `OllamaApiClient`
`OllamaApiClient` is sealed and has no default constructor — NSubstitute cannot mock it. `IOllamaApiClient` is provided by OllamaSharp and already used by `OllamaLlmClassifier`. DI registration is unaffected because `OllamaApiClient` implements `IOllamaApiClient`.

### D2: `[ExcludeFromCodeCoverage]` attribute over coverlet filter config
Attribute is self-documenting: readers can see at a glance why a class is excluded. Applied to Qdrant infrastructure (`QdrantVectorStoreService`, `QdrantSearchClientAdapter`, `QdrantCollectionInitializer`, `QdrantHealthCheck`), DI-wiring (`ServiceCollectionExtensions`, `WebApplicationExtensions`), and simple option types (`OpenTelemetryOptions`, `RegisterBookRequest`). `Program.cs` uses top-level statements so the attribute cannot be applied; handle via coverlet `ExcludeByFile` in the test `.csproj`.

### D3: `PdfDocumentBuilder` for in-memory PDFs
Avoids fixture files on disk. Same pattern as `PdfPigBookmarkReaderTests`. Tests for multi-page, single-page, and sparse (short-text) page to exercise all branches.

### D4: WebApplication + `UseTestServer` for `RetrievalEndpoints`
Same pattern established by `BooksAdminEndpointsTests`. The `IRagRetrievalService` is registered as an NSubstitute mock. Admin diagnostic endpoint requires `X-Admin-Api-Key` header (value configured via `Admin:ApiKey` in the test builder).

### D5: No Qdrant in `RetrievalEndpoints` tests
`IRagRetrievalService` is the boundary. Its concrete implementation talks to Qdrant; in tests it is fully mocked. No `QdrantClient` or Qdrant container needed.

## Risks / Trade-offs

- [PdfDocumentBuilder text layout] PdfPig's word-extraction groups by Y position; in-memory PDFs built with `PdfDocumentBuilder` may produce different spatial layout than real PDFs → Mitigation: assert on presence of known words rather than exact whitespace layout.
- [OllamaSharp interface stability] `IOllamaApiClient` surface area could change in future OllamaSharp releases → Low risk; already used in production code.
- [Coverage number] `Program.cs` exclusion affects denominator but not numerator; reported % may shift slightly → Expected and acceptable.
