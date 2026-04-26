## Why

Qdrant is populated with embedded D&D chunks carrying version, category, and source metadata. The system now needs a retrieval layer that turns a natural-language query into a vector search with optional metadata filters, returning ranked relevant chunks. This is the internal service the MCP layer will call — it must support filtering by edition (2014/2024), category (spell/monster/etc.), and source book, and return enough context for an AI to compose a useful answer.

## What Changes

- Add `IRagRetrievalService` with a `SearchAsync` method accepting a `RetrievalQuery` (query text, optional filters, top-k)
- Implement `RagRetrievalService` that embeds the query via `IEmbeddingService` then performs a filtered Qdrant vector search
- Add `RetrievalQuery` record with filter fields: `Version`, `Category`, `SourceBook`, `EntityName`, `TopK`
- Add `RetrievalResult` record representing one matched chunk with its metadata and similarity score
- Add `GET /retrieval/search` internal HTTP endpoint for direct testing of the retrieval service
- Add admin endpoint `GET /admin/retrieval/search` with full filter support for operational diagnostics

## Capabilities

### New Capabilities

- `rag-retrieval`: Vector similarity search over Qdrant with optional payload filters on version, category, source book, and entity name

### Modified Capabilities

## Impact

- `Features/Retrieval/` — new home for retrieval service and models
- `Program.cs` — registers `IRagRetrievalService`, maps retrieval endpoints
- No new NuGet packages — uses existing `IEmbeddingService` and `QdrantClient`
- Depends on: `foundation`, `ingestion-pipeline`, `embedding-vector-store` (Qdrant must have data)
