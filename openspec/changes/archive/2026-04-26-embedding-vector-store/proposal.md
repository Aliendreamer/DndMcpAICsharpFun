## Why

The ingestion pipeline produces `ContentChunk` objects with rich metadata but has nowhere to store them — `IEmbeddingIngestor` is currently a stub. This change replaces that stub with a real implementation: embed each chunk's text via Ollama (nomic-embed-text), then upsert the vector plus payload into Qdrant. The result is a queryable vector store that the RAG retrieval change can search against.

## What Changes

- Implement `OllamaEmbeddingService` that calls Ollama's `/api/embed` endpoint via OllamaSharp and returns a float vector per text input
- Implement `QdrantVectorStoreService` that creates/ensures the DnD collection exists and upserts points with full `ChunkMetadata` as Qdrant payload
- Replace the stub `IEmbeddingIngestor` with `EmbeddingIngestor` that combines embedding + storage in a single unit-of-work per chunk batch
- Add Qdrant collection initialisation on startup (creates the collection if it does not exist, with correct vector size for `nomic-embed-text`)
- Add `IEmbeddingService` and `IVectorStoreService` interfaces enabling future model or store swaps

## Capabilities

### New Capabilities

- `ollama-embedding`: Text → float vector via OllamaSharp, with model name from configuration
- `qdrant-storage`: Upsert chunks as Qdrant points with ChunkMetadata as filterable payload; collection auto-created on startup

### Modified Capabilities

- `ingestion-tracking`: Stub `IEmbeddingIngestor` is replaced by real implementation — no spec-level requirement changes, only the no-op is removed

## Impact

- `Features/Embedding/` — new `OllamaEmbeddingService`, `EmbeddingIngestor`
- `Features/VectorStore/` — new `QdrantVectorStoreService`, `QdrantCollectionInitializer`
- `Program.cs` — registers new services, triggers collection initialisation on startup
- `DndMcpAICsharpFun.csproj` — no new NuGet packages (OllamaSharp and Qdrant.Client already added in `foundation`)
- Depends on: `foundation` (clients in DI) and `ingestion-pipeline` (`ContentChunk`, `ChunkMetadata`, `IEmbeddingIngestor` interface)
