# ingestion-pipeline (delta)

## ADDED Requirements

### Requirement: Docling is reachable when block ingestion uses it
When `Ingestion:BlockSegmenter` is `"docling"`, the system SHALL require docling-serve to be reachable at `Docling:BaseUrl`. If docling-serve is unhealthy or unreachable at the time `IngestBlocksAsync` runs, the orchestrator SHALL mark the record `Failed` with an error message identifying docling-serve as the failing dependency, and SHALL NOT write any points to Qdrant.

#### Scenario: Docling unreachable during ingestion fails the record cleanly
- **WHEN** block ingestion runs with `BlockSegmenter=docling` and docling-serve is down
- **THEN** the orchestrator marks the record `Failed` with an error message that names docling-serve, the configured `Docling:BaseUrl`, and the underlying HTTP error

#### Scenario: Health check surfaces docling outages
- **WHEN** the application's `/ready` endpoint is hit while docling-serve is down
- **THEN** the response includes a failed `docling` health-check entry, regardless of whether the running ingestion mode actually uses docling
