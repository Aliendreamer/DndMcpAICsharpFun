## ADDED Requirements

### Requirement: Embedding service produces float vectors from text input
The system SHALL provide an `IEmbeddingService` with a method accepting a list of text strings and returning a corresponding list of float arrays (one vector per input), using the model configured in `OllamaOptions:EmbeddingModel`.

#### Scenario: Single text input returns one vector
- **WHEN** `IEmbeddingService.EmbedAsync` is called with one text string
- **THEN** a single float array of the correct dimensionality (768 for nomic-embed-text) is returned

#### Scenario: Batch input returns one vector per input in order
- **WHEN** `IEmbeddingService.EmbedAsync` is called with N text strings
- **THEN** exactly N float arrays are returned in the same order as the inputs

#### Scenario: Ollama unavailable throws descriptive exception
- **WHEN** the Ollama service is unreachable during embedding
- **THEN** an exception is thrown with a message identifying Ollama as the failure point

### Requirement: Embedding ingestor processes chunks in configurable batches
The system SHALL implement `IEmbeddingIngestor` using `IEmbeddingService` in batches of `IngestionOptions:EmbeddingBatchSize` chunks, then upsert each batch to the vector store.

#### Scenario: Large chunk list is split into batches
- **WHEN** `IEmbeddingIngestor.IngestAsync` is called with more chunks than `EmbeddingBatchSize`
- **THEN** embedding is called once per batch, not once per chunk

#### Scenario: Partial batch failure does not lose completed batches
- **WHEN** embedding fails on the third batch of five
- **THEN** the first two batches are already upserted to Qdrant and are not lost
