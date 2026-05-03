## ADDED Requirements

### Requirement: OllamaEmbeddingService depends on IOllamaApiClient
`OllamaEmbeddingService` SHALL accept `IOllamaApiClient` in its constructor instead of `OllamaApiClient`.

#### Scenario: Constructor uses interface type
- **WHEN** `OllamaEmbeddingService` is registered in DI
- **THEN** it can be resolved with `OllamaApiClient` (which implements `IOllamaApiClient`)

### Requirement: OllamaHealthCheck depends on IOllamaApiClient
`OllamaHealthCheck` SHALL accept `IOllamaApiClient` in its constructor instead of `OllamaApiClient`.

#### Scenario: Constructor uses interface type
- **WHEN** `OllamaHealthCheck` is registered in DI
- **THEN** it can be resolved with `OllamaApiClient` (which implements `IOllamaApiClient`)
