# embedding-service-tests Specification

## Purpose
TBD - created by archiving change test-coverage-wave-2. Update Purpose after archive.
## Requirements
### Requirement: OllamaEmbeddingService returns embeddings on success
`OllamaEmbeddingService.EmbedAsync` SHALL return the `Embeddings` array from the Ollama response when the client call succeeds.

#### Scenario: Successful embed returns embeddings
- **WHEN** `EmbedAsync` is called with a list of texts
- **THEN** the Ollama client's `EmbedAsync` is called once with the correct model
- **AND** the returned list matches the embeddings from the response

### Requirement: OllamaEmbeddingService wraps HTTP errors
`OllamaEmbeddingService.EmbedAsync` SHALL wrap `HttpRequestException` as `InvalidOperationException` whose message contains the configured model name.

#### Scenario: HttpRequestException becomes InvalidOperationException
- **WHEN** `EmbedAsync` is called and the Ollama client throws `HttpRequestException`
- **THEN** an `InvalidOperationException` is thrown
- **AND** the exception message contains the model name
- **AND** the original `HttpRequestException` is the inner exception

