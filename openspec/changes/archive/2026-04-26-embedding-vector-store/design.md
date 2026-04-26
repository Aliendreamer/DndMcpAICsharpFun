## Context

OllamaSharp and `Qdrant.Client` are already registered in DI (foundation change). `ContentChunk` and `ChunkMetadata` domain types exist (ingestion-pipeline change). `IEmbeddingIngestor` is a no-op stub that the orchestrator calls with each batch of chunks after chunking completes.

The embedding model is `nomic-embed-text`, the standard free local model for semantic search. It produces 768-dimensional vectors. The Qdrant collection must be created with this dimensionality and cosine distance before any upserts.

## Goals / Non-Goals

**Goals:**
- `IEmbeddingService`: thin wrapper over OllamaSharp `/api/embed`, injectable and swappable
- `IVectorStoreService`: thin wrapper over `Qdrant.Client`, injectable and swappable
- `EmbeddingIngestor`: implements `IEmbeddingIngestor`, batches chunks → embed → upsert
- Collection auto-creation on startup with correct vector config
- All `ChunkMetadata` fields stored as Qdrant point payload for filtering

**Non-Goals:**
- Retrieval / search (handled in `rag-retrieval` change)
- Support for multiple embedding models simultaneously
- Qdrant collection sharding or replication config
- Deduplication of already-stored vectors (ingestion tracker handles this at file level)

## Decisions

### D1 — Single Qdrant collection named `dnd_chunks`

All books, versions, and categories go into one collection. Filtering by `version`, `category`, `source_book` etc. is done at query time via Qdrant payload filters.

Alternatives considered:
- Separate collection per book/version: simpler per-collection queries but complex cross-version retrieval; no single query can span editions
- Separate collection per category: same cross-category problem

Rationale: One collection with rich payload is the standard Qdrant pattern for RAG. Payload filtering is efficient at Qdrant's side and supports the "give me Fireball, 2024 rules" query pattern directly.

### D2 — Vector size 768 for nomic-embed-text, cosine distance

`nomic-embed-text` outputs 768-dimensional vectors. Cosine distance is standard for semantic similarity. These values are hardcoded in `QdrantOptions` defaults but overridable via config.

### D3 — Point ID is deterministic from file hash + chunk index

```
pointId = Guid derived from SHA256(fileHash + chunkIndex)
```

Rationale: Deterministic IDs mean re-ingesting the same file (reingest endpoint) produces the same point IDs, allowing Qdrant upsert to overwrite stale vectors rather than accumulate duplicates.

### D4 — Batched embedding calls (configurable batch size)

Chunks are embedded in batches (default: 32) rather than one at a time. OllamaSharp supports batching via the embed endpoint's `input` array.

Rationale: Reduces round-trips to Ollama significantly for large books (600+ pages = thousands of chunks).

### D5 — Collection initialisation as IHostedService startup task

A `QdrantCollectionInitializer` hosted service runs `StartAsync` once: checks if `dnd_chunks` exists, creates it if not. Runs before `IngestionBackgroundService`.

Rationale: Decoupled from ingestion. If Qdrant is unavailable at startup, the health check catches it; the initializer will log the failure without crashing the app.

### D6 — All ChunkMetadata fields indexed as Qdrant payload

Payload fields stored and indexed for filtering:
- `source_book` (keyword)
- `version` (keyword)
- `category` (keyword)
- `entity_name` (keyword)
- `chapter` (keyword)
- `page_number` (integer)
- `chunk_index` (integer)

Rationale: Keyword indexes enable fast filtered search in Qdrant. All fields the retrieval change will need for filtering should be indexed now.

## Risks / Trade-offs

- **nomic-embed-text not pulled in Ollama container** → Qdrant initializer can succeed but embedding will fail at runtime. Mitigation: add Ollama model pull to Docker Compose `ollama` service entrypoint or document as a manual step.
- **768 vector size hardcoded** → Changing embedding model later requires dropping and recreating the collection. Mitigation: document the constraint; configurable via `QdrantOptions:VectorSize`.
- **Batched embed with Ollama may OOM for very large batches** → Default batch size of 32 is conservative. Mitigation: configurable via `IngestionOptions:EmbeddingBatchSize`.
- **Upsert overwrites on reingest** → Desired behaviour, but if chunk boundaries change between ingests, stale orphan points may remain. Mitigation: accepted for now; a full collection wipe via admin endpoint can be added later.

## Migration Plan

1. Create `IEmbeddingService` and `OllamaEmbeddingService`
2. Create `IVectorStoreService` and `QdrantVectorStoreService`
3. Create `QdrantCollectionInitializer` hosted service
4. Implement `EmbeddingIngestor` replacing the no-op stub
5. Register all new services in `Program.cs`; ensure collection initializer runs before ingestion service
6. Test: ingest one small PDF, verify points appear in Qdrant with correct payload
