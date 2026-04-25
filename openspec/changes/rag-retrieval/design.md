## Context

`IEmbeddingService` can embed query text into a float vector. `QdrantClient` is registered and the `dnd_chunks` collection exists with all `ChunkMetadata` fields indexed as payload. This change wires them together into a retrieval service that the MCP layer (future change) will consume.

## Goals / Non-Goals

**Goals:**
- `IRagRetrievalService.SearchAsync(RetrievalQuery)` → `IList<RetrievalResult>`
- Filter support: `Version`, `Category`, `SourceBook`, `EntityName` (all optional, ANDed together)
- Configurable `TopK` (default 5, max 20)
- Score threshold: discard results below a minimum cosine similarity
- HTTP endpoint for direct testing: `GET /retrieval/search?q=fireball&version=2024&category=spell`
- Admin diagnostic endpoint: `GET /admin/retrieval/search` with full filter set

**Non-Goals:**
- Re-ranking or cross-encoder scoring
- Query expansion or HyDE (hypothetical document embeddings)
- Caching of query embeddings
- Streaming results

## Decisions

### D1 — Qdrant `SearchAsync` with inline payload filter construction

Build the Qdrant `Filter` object programmatically from non-null `RetrievalQuery` filter fields. Each non-null field becomes a `FieldCondition` (keyword match). All conditions are combined with `Must` (AND semantics).

```csharp
// Example filter for version=2024, category=spell
new Filter {
  Must = [
    new Condition { Field = new FieldCondition { Key = "version", Match = new Match { Keyword = "2024" } } },
    new Condition { Field = new FieldCondition { Key = "category", Match = new Match { Keyword = "spell" } } }
  ]
}
```

Rationale: Qdrant's native filter API is expressive and efficient. Building it from the query model keeps the retrieval service self-contained without additional query DSL.

### D2 — Score threshold configurable via `RetrievalOptions`

`RetrievalOptions:ScoreThreshold` (default: 0.5). Results below this cosine similarity are excluded even if `TopK` is not yet satisfied.

Rationale: Low-similarity results are noise in a RAG context. 0.5 is a conservative default for cosine similarity with `nomic-embed-text`; operators can tune it per deployment.

### D3 — Query embedding is always single-item embed call

The query is one string. Call `IEmbeddingService.EmbedAsync([queryText])` and take the first result.

### D4 — Public `GET /retrieval/search` endpoint (no auth)

The retrieval endpoint is intentionally unauthenticated — it only reads from Qdrant, returns no sensitive data, and will be the primary endpoint the MCP layer calls. Rate limiting is a future concern.

The admin `GET /admin/retrieval/search` adds the API key requirement and exposes internal fields (point IDs, raw scores) useful for diagnostics.

### D5 — RetrievalResult maps Qdrant payload back to ChunkMetadata

```csharp
record RetrievalResult(
    string Text,
    ChunkMetadata Metadata,
    float Score
);
```

Payload fields are mapped back to `ChunkMetadata` on the way out. The MCP tools receive fully typed results, not raw dictionaries.

## Risks / Trade-offs

- **Query embedding round-trip adds latency** → Every search calls Ollama. On local hardware this may be 100-500ms. Acceptable for MCP tool use; can be cached later if needed.
- **TopK=5 may miss relevant results for broad queries** → Exposed as a query parameter; callers can increase it. Max capped at 20 to bound Qdrant result set size.
- **Score threshold may be too aggressive for sparse collections** → During early development (few books ingested), lower the threshold. Default 0.5 is tunable via config.
- **No auth on `/retrieval/search`** → Acceptable for a local Docker deployment. Should be reconsidered before any public exposure.

## Migration Plan

1. Create `RetrievalQuery` and `RetrievalResult` records
2. Create `RetrievalOptions` and bind from config
3. Create `IRagRetrievalService` interface
4. Implement `RagRetrievalService` (embed query → build filter → Qdrant search → map results)
5. Register service and options in `Program.cs`
6. Map `GET /retrieval/search` and `GET /admin/retrieval/search` endpoints
7. Test: with a populated Qdrant collection, query for "fireball spell 2024" and verify ranked results
