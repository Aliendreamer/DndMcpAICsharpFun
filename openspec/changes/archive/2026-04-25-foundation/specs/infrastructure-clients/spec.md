## ADDED Requirements

### Requirement: Qdrant client is registered in DI
The system SHALL register a `QdrantClient` instance in the DI container, configured from `appsettings.json` under the `Qdrant` section (host, port, API key optional).

#### Scenario: Client resolves from DI
- **WHEN** a service requests `QdrantClient` from the DI container
- **THEN** a configured instance is returned without error

#### Scenario: Missing Qdrant config throws on startup
- **WHEN** the `Qdrant:Host` configuration value is absent
- **THEN** the application fails to start with a descriptive configuration error

### Requirement: Ollama client is registered in DI
The system SHALL register an `OllamaApiClient` (OllamaSharp) instance in the DI container, configured from `appsettings.json` under the `Ollama` section (base URL, embedding model name).

#### Scenario: Client resolves from DI
- **WHEN** a service requests `OllamaApiClient` from the DI container
- **THEN** a configured instance is returned without error

### Requirement: PdfPig document reader is available
The system SHALL make `PdfPig` available for injection via a factory, configured for text-layer extraction.

#### Scenario: PDF reader is resolvable
- **WHEN** the ingestion pipeline requests a PDF reader instance
- **THEN** a PdfPig-backed reader is returned

### Requirement: Typed options classes exist for all infrastructure
The system SHALL provide `QdrantOptions`, `OllamaOptions`, and `IngestionOptions` strongly-typed classes bound via `IOptions<T>`.

#### Scenario: Options are populated from config
- **WHEN** `IOptions<QdrantOptions>` is resolved
- **THEN** its properties reflect the values in `appsettings.json`

### Requirement: Health checks report Qdrant and Ollama reachability
The system SHALL expose a `GET /health/ready` endpoint that checks connectivity to both Qdrant and Ollama.

#### Scenario: Both dependencies healthy
- **WHEN** Qdrant and Ollama are reachable
- **THEN** `GET /health/ready` returns HTTP 200 with status `Healthy`

#### Scenario: One dependency unreachable
- **WHEN** Qdrant or Ollama is not reachable
- **THEN** `GET /health/ready` returns HTTP 503 with status `Unhealthy` and identifies which dependency failed
