## 1. Fix IOllamaApiClient Abstraction

- [ ] 1.1 Change `OllamaEmbeddingService` constructor parameter from `OllamaApiClient client` to `IOllamaApiClient client`
- [ ] 1.2 Change `OllamaHealthCheck` constructor parameter from `OllamaApiClient client` to `IOllamaApiClient client`
- [ ] 1.3 Build the project to confirm DI registration still compiles (`dotnet build`)

## 2. Coverage Exclusions

- [ ] 2.1 Add `[ExcludeFromCodeCoverage]` to `QdrantVectorStoreService`, `QdrantSearchClientAdapter`, `QdrantCollectionInitializer`, `QdrantHealthCheck`
- [ ] 2.2 Add `[ExcludeFromCodeCoverage]` to `ServiceCollectionExtensions`, `WebApplicationExtensions`, `OpenTelemetryOptions`, `RegisterBookRequest`
- [ ] 2.3 Add `ExcludeByFile` coverlet MSBuild property to the test `.csproj` to exclude `**/Program.cs`

## 3. PdfPigTextExtractor Tests

- [ ] 3.1 Create `DndMcpAICsharpFun.Tests/Ingestion/Pdf/PdfPigTextExtractorTests.cs`
- [ ] 3.2 Add test: single-page PDF returns one tuple with correct `PageNumber` and text containing known words
- [ ] 3.3 Add test: three-page PDF returns three tuples with `PageNumber` values 1, 2, 3
- [ ] 3.4 Add test: empty PDF (no pages) returns empty enumerable
- [ ] 3.5 Add test: page with text below `MinPageCharacters` triggers Debug log entry
- [ ] 3.6 Add test: page with sufficient text does not trigger sparse-page log
- [ ] 3.7 Run `dotnet test` and confirm all new tests pass

## 4. OllamaEmbeddingService Tests

- [ ] 4.1 Create `DndMcpAICsharpFun.Tests/Embedding/OllamaEmbeddingServiceTests.cs`
- [ ] 4.2 Add test: `EmbedAsync` calls `client.EmbedAsync` once and returns the embeddings array from the response
- [ ] 4.3 Add test: `EmbedAsync` wraps `HttpRequestException` as `InvalidOperationException` whose message contains the model name and whose `InnerException` is the original
- [ ] 4.4 Run `dotnet test` and confirm all new tests pass

## 5. OllamaHealthCheck Tests

- [ ] 5.1 Create `DndMcpAICsharpFun.Tests/Ollama/OllamaHealthCheckTests.cs`
- [ ] 5.2 Add test: `CheckHealthAsync` returns `HealthStatus.Healthy` when `ListLocalModelsAsync` completes
- [ ] 5.3 Add test: `CheckHealthAsync` returns `HealthStatus.Unhealthy` with description "Ollama is unreachable" and the thrown exception attached when `ListLocalModelsAsync` throws
- [ ] 5.4 Run `dotnet test` and confirm all new tests pass

## 6. RetrievalEndpoints Tests

- [ ] 6.1 Create `DndMcpAICsharpFun.Tests/Retrieval/RetrievalEndpointsTests.cs`
- [ ] 6.2 Add test: `GET /retrieval/search` with no `q` returns 400
- [ ] 6.3 Add test: `GET /retrieval/search` with whitespace `q` returns 400
- [ ] 6.4 Add test: `GET /retrieval/search?q=fireball` returns 200 and calls `SearchAsync` once
- [ ] 6.5 Add test: valid `version=Edition2024&category=Spell` query params are parsed and passed to `SearchAsync`
- [ ] 6.6 Add test: invalid `version=invalid&category=invalid` params result in `null` Version and Category passed to `SearchAsync`
- [ ] 6.7 Add test: `GET /admin/retrieval/search?q=fireball` without `X-Admin-Api-Key` returns 401
- [ ] 6.8 Add test: `GET /admin/retrieval/search?q=fireball` with valid `X-Admin-Api-Key` returns 200 and calls `SearchDiagnosticAsync` once
- [ ] 6.9 Run `dotnet test` and confirm all new tests pass

## 7. Final Verification

- [ ] 7.1 Run full test suite (`dotnet test`) — all tests pass
- [ ] 7.2 Generate coverage report and verify `PdfPigTextExtractor`, `OllamaEmbeddingService`, `OllamaHealthCheck`, and `RetrievalEndpoints` now have meaningful coverage
- [ ] 7.3 Confirm Qdrant classes and DI-wiring classes are absent from the coverage report
