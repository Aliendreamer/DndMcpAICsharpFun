# infrastructure-clients (delta)

## ADDED Requirements

### Requirement: A typed HTTP client wraps docling-serve
The system SHALL expose `IDoclingPdfConverter` registered as a Singleton, owning a single `HttpClient` instance configured with `BaseAddress = Docling:BaseUrl` and `Timeout = TimeSpan.FromSeconds(Docling:RequestTimeoutSeconds)`. The client SHALL be reused across all conversion requests in the application's lifetime; per-request HttpClients SHALL NOT be created.

#### Scenario: HttpClient is reused
- **WHEN** `IngestBlocksAsync` runs sequentially against multiple books with `BlockSegmenter=docling`
- **THEN** all conversions use the same `HttpClient` instance (no socket exhaustion, no per-call connection setup beyond keep-alive renewal)

#### Scenario: Configuration is bound from the Docling section
- **WHEN** the application starts
- **THEN** `DoclingOptions` is populated from the `Docling` configuration section with defaults `BaseUrl = "http://docling:5001"` and `RequestTimeoutSeconds = 600`
