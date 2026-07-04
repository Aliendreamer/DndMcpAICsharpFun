## Why

Both Qdrant collections (`dnd_blocks`, `dnd_entities`) store dense `mxbai-embed-large` vectors as raw float32 with **no quantization** — 1024 dims × 4 bytes ≈ **4 KB/vector**, held in RAM. As the corpus grows (more D&D books × prose chunks + typed entities), vector memory and search latency scale linearly. **Scalar int8 quantization** cuts vector memory ~**4×** (to ~1 KB/vector) and speeds HNSW traversal, while **rescoring** against the original vectors preserves recall. It is low-risk and applies **in place** — Qdrant re-quantizes an existing collection via `update_collection` in the background, so no re-ingestion or downtime is required. This was the one high-value optimization surfaced by the Qdrant usage review.

## What Changes

- Add **configurable scalar int8 quantization** (`always_ram`, quantile 0.99) to the dense vectors of both collections.
- Preserve recall with **rescoring + oversampling** on search (search the quantized index, then rescore the top candidates against the original vectors).
- `QdrantOptions.Quantization` toggle (**default on**) so it can be disabled without code changes.
- Apply quantization to **new** collections at creation and **in place** to existing collections at startup (idempotent: skip if already configured).

## Capabilities

### New Capabilities
- `qdrant-vector-quantization`: Configurable scalar int8 quantization of the dense vectors in the Qdrant collections, with rescoring to preserve recall and an idempotent in-place rollout.

## Impact

- **Config**: `Infrastructure/Qdrant/QdrantOptions.cs` (+`Quantization` options: enabled, always-ram, quantile, rescore, oversampling).
- **Collection setup**: `QdrantCollectionInitializer` — set `QuantizationConfig` on `VectorParams` for new collections; `update_collection` for existing ones (guarded by an "already quantized?" check).
- **Search**: quantized search params (rescore=true, oversampling) in `QdrantSearchClientAdapter` / the vector-store search calls, so recall is preserved.
- **No re-ingestion, no downtime**; sparse vectors (hybrid) are unaffected.
- **Out of scope**: binary/product quantization; on-disk/mmap vector storage; HNSW `m`/`ef_construct` tuning (separate levers).
