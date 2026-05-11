## Context

The current RAG pipeline uses pure dense vector search via Qdrant (cosine similarity on `mxbai-embed-large` embeddings, 1024 dimensions). Queries like "what does the Sentinel feat do" work well semantically but queries for exact proper nouns — spell names, monster names, feat names — can rank poorly because embeddings encode distributional meaning, not orthographic identity. BM25 term-frequency scoring complements dense search precisely where it is weakest.

Qdrant natively supports sparse vectors alongside dense vectors in the same collection and provides `Query` API hybrid search with Reciprocal Rank Fusion (RRF) or score fusion. No external service is required.

The existing `dnd_blocks` collection has dense vectors only. This change adds a sparse vector named `"text-sparse"` alongside the existing unnamed dense vector, computes BM25 sparse representations at ingestion time, and updates all query paths to use hybrid search.

## Goals / Non-Goals

**Goals:**
- Produce BM25 sparse vectors for every ingested block and upsert them alongside dense vectors
- Enable hybrid search on `dnd_blocks` using Qdrant's built-in `Query` API with RRF fusion
- Migrate the existing collection to support sparse vectors without data loss
- Expose a configurable `Qdrant:HybridAlpha` weight (0.0 = pure BM25, 1.0 = pure dense, default 0.5)
- Keep all existing MCP tool signatures and API contracts unchanged

**Non-Goals:**
- SPLADE or other neural sparse models — BM25 only
- Per-query alpha tuning exposed to callers
- Adding hybrid search to the entity collection (block search first)
- Changing chunk size, embedding model, or payload schema

## Decisions

### BM25 implementation — in-process vs. Qdrant built-in

Qdrant does not compute sparse vectors for you; the client must supply them. We compute BM25 in the C# app at ingestion time using a simple IDF-weighted term frequency implementation over the corpus vocabulary.

**Alternative considered:** integrate a third-party BM25 library. Rejected — BM25 is ~30 lines of math; a dependency is not justified.

**IDF strategy:** compute IDF over the documents seen within a single ingestion run (batch IDF). A global corpus-wide IDF would require a two-pass ingestion. Batch IDF is a known approximation; for a domain-specific corpus of D&D text with consistent vocabulary it is sufficient.

### Fusion method — RRF vs. linear score fusion

Qdrant's `Query` API supports both Reciprocal Rank Fusion (RRF) and linear score combination. RRF is rank-based and robust to score scale differences between dense and sparse vectors. Linear fusion requires careful normalisation of scores from two different distributions.

**Decision:** use RRF. No normalisation needed, more robust, recommended by Qdrant for mixed-modality search.

`HybridAlpha` controls the weight passed to RRF's `rank_constant` equivalent — higher values weight dense results more heavily.

### Collection migration — recreate vs. add named vector in-place

Qdrant does not support adding a new named vector to an existing collection in-place (as of v1.13). The collection must be recreated with the sparse vector config, and all points re-upserted.

**Decision:** On startup, if `dnd_blocks` exists without a sparse vector config, log a warning and proceed with dense-only search until the operator triggers a full re-ingestion. The ingestion pipeline will detect the missing sparse config and recreate the collection automatically on next run. This avoids a forced destructive migration on every startup.

### Sparse vector format

Qdrant sparse vectors are `{ indices: int[], values: float[] }` pairs. BM25 produces a term → score map; we convert to sorted index arrays using a vocabulary hash (`term → abs(term.GetHashCode()) % MaxVocabSize`, `MaxVocabSize = 30000`).

## Risks / Trade-offs

[Hash collision in vocabulary] → Mitigation: `MaxVocabSize = 30000` gives low collision probability for D&D vocabulary (~5000 distinct stems). Collisions degrade precision slightly but do not cause errors.

[Batch IDF approximation] → Mitigation: D&D corpus has stable vocabulary across books; batch IDF is consistent enough for this domain.

[Collection recreation required for migration] → Mitigation: operator re-runs ingestion after deploying; existing dense-only search continues working until then via fallback.

[Sparse vector upsert increases ingestion payload size] → Mitigation: sparse vectors are compact (only non-zero terms stored); typical D&D block has ~50-200 non-zero terms, well within Qdrant limits.

## Migration Plan

1. Deploy updated app — existing collection continues to serve dense-only queries
2. Re-run ingestion for all books — pipeline detects missing sparse config, recreates collection with `text-sparse` named vector, upserts all points with both dense and sparse vectors
3. Hybrid search activates automatically once collection has sparse vectors
4. Rollback: set `Qdrant:HybridAlpha = 1.0` to effectively use only dense scores, or revert to previous image which skips sparse entirely
