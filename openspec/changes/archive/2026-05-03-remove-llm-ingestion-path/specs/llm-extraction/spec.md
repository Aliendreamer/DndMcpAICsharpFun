# llm-extraction (delta)

## REMOVED Requirements

### Requirement: ILlmEntityExtractor extracts structured entities from page text
**Reason**: The per-page LLM entity extraction pipeline is removed. Empirical results on a real PHB showed only spell entities were reliably produced; monsters, classes, rules, conditions, and other categories came back empty or scrambled, while consuming hours of inference per book. The block-ingestion path delivers comparable retrieval quality for the MCP-as-RAG use case in minutes per book without LLM cost during ingestion.
**Migration**: No callers remain — `OllamaLlmEntityExtractor` was used only by the `IngestionOrchestrator.ExtractBookAsync` path that is also being removed. Configuration keys `Ollama:ExtractionModel`, `Ollama:ExtractionNumCtx`, `Ollama:ExtractionTimeoutSeconds`, and `Ingestion:LlmExtractionRetries` are removed from `appsettings.json` and the corresponding C# option classes; deployments setting them via env vars or compose files should remove those overrides.

### Requirement: ILlmEntityExtractor retries on invalid JSON
**Reason**: Subsumed by the removal of the entity extractor itself. With no extractor, retry behaviour is moot.
**Migration**: None.

### Requirement: Entity extractor unwraps single-key JSON objects
**Reason**: Subsumed by the removal of the entity extractor itself.
**Migration**: None.

### Requirement: ExtractedEntity carries section-level fields
**Reason**: `ExtractedEntity` is deleted. The block-ingestion path uses `BlockChunk` + `BlockMetadata` instead, which carry their own `SectionTitle`, `SectionStart`, `SectionEnd` fields.
**Migration**: None — `ExtractedEntity` was an internal type. The `dnd_chunks` Qdrant collection that it ultimately populated is also no longer created (see `embedding-vector-store` delta).

### Requirement: EntityJsonStore persists per-page extracted entities to disk
**Reason**: The on-disk JSON intermediate (`books/extracted/{bookId}/page_*.json`) was the staging step between LLM extraction and the embedding pass. With the LLM path removed there is no producer, so the store has no consumer either.
**Migration**: Existing `books/extracted/` directories on disk are orphaned and can be deleted by operators (`docker compose exec app rm -rf /books/extracted` or the host-side equivalent if a bind mount is used).

### Requirement: A merge pass joins partial entities across pages
**Reason**: `IEntityJsonStore.RunMergePassAsync` only ever ran inside the JSON ingestion pipeline that is being removed. Block ingestion does not produce `partial: true` entities, so cross-page merging is no longer applicable.
**Migration**: None.

### Requirement: Single-page extraction is exposed for debugging
**Reason**: The `POST /admin/books/{id}/extract-page/{pageNumber}` endpoint and the underlying `IIngestionOrchestrator.ExtractSinglePageAsync` method are removed. Their purpose was to inspect what the LLM produced for one page in isolation; with no LLM ingestion, the use case disappears.
**Migration**: For block-level inspection, query Qdrant directly with a `page_number` filter (`http://localhost:6333/collections/dnd_blocks/points/scroll` with the appropriate filter body).
