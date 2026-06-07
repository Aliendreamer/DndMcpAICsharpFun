## MODIFIED Requirements

### Requirement: Docling is reachable when block ingestion uses it
Block ingestion SHALL require the marker service to be reachable at `Marker:Url`. If the marker service is unhealthy or unreachable at the time `IngestBlocksAsync` runs, the orchestrator SHALL mark the record `Failed` with an error message identifying the marker service as the failing dependency, and SHALL NOT write any points to Qdrant.

#### Scenario: Marker unreachable during ingestion fails the record cleanly

- **WHEN** block ingestion runs while the marker service is down
- **THEN** the orchestrator marks the record `Failed` with an error message that names the marker service, the configured `Marker:Url`, and the underlying HTTP error
- **AND** no points are written to Qdrant
