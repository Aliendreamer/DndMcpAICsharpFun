## Why

Pure vector/semantic search struggles with exact name lookups — spell names, feat names, monster names — because embeddings encode meaning, not spelling. Adding BM25 keyword scoring alongside dense vector search lets Qdrant combine both signals, improving precision for named entity queries without sacrificing semantic recall.

## What Changes

- Add sparse vector fields to Qdrant collections alongside existing dense vectors
- Compute BM25 sparse vectors at ingestion time using term frequency / IDF weighting
- Update all query paths to issue hybrid queries (dense + sparse) with a weighted combination
- Expose a configurable fusion weight so dense vs. sparse balance can be tuned per-environment

## Capabilities

### New Capabilities

- `hybrid-retrieval`: Qdrant hybrid search combining dense embedding vectors with BM25 sparse vectors; configurable alpha weight; applied to all existing collections

### Modified Capabilities

- `rag-retrieval`: retrieval behaviour changes — queries now return hybrid-ranked results instead of pure vector similarity results
- `embedding-vector-store`: collections gain a sparse vector field; ingestion pipeline produces and upserts sparse vectors alongside dense vectors

## Impact

- `DndMcpAICsharpFun` — ingestion pipeline, Qdrant client, RAG retrieval MCP tools
- Qdrant collections must be migrated to add sparse vector support (handled via payload index + named vectors)
- No breaking changes to MCP tool signatures or API contracts
- New NuGet dependency: none (Qdrant .NET SDK already supports sparse vectors)
