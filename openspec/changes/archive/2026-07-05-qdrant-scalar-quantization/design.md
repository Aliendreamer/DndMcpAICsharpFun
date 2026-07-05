## Context

`QdrantCollectionInitializer` creates both collections with `new VectorParams { Size, Distance = Cosine }` and no `QuantizationConfig`. Search goes through `QdrantSearchClientAdapter` (dense `SearchAsync` and hybrid `QueryAsync` with `Fusion.Rrf` + dense/sparse prefetch). Vectors are `mxbai-embed-large` (1024-dim, cosine, normalized). Qdrant supports adding quantization to a live collection via `update_collection`, which re-quantizes existing vectors in the background — no re-ingestion.

## Goals / Non-Goals

**Goals:**
- ~4× lower dense-vector memory and faster search via scalar int8 quantization.
- Preserve retrieval recall via rescoring against original vectors.
- In-place, idempotent rollout (new + existing collections); toggleable; no re-ingest, no downtime.

**Non-Goals:**
- Binary or product quantization; on-disk/mmap storage; HNSW `m`/`ef_construct` tuning.
- Quantizing sparse vectors (they are not float-dense; unaffected).

## Decisions

- **Scalar int8, not binary/product.** Scalar int8 is the sweet spot for 1024-dim RAG: ~4× compression with near-lossless recall when paired with rescoring. Binary (32×) is too aggressive for recall-sensitive RAG; product quantization adds training/complexity for little extra gain at this dim. *Alternative considered:* binary + strong oversampling — rejected as risky for answer quality.
- **`always_ram: true` for the quantized vectors; originals stay for rescoring.** Quantized vectors (1 KB) live in RAM for fast traversal; the original float32 vectors are retained (Qdrant keeps them, on disk by default) and used only to rescore the shortlist. This is the standard latency-and-recall-preserving setup.
- **Rescoring + oversampling on search.** Search retrieves an oversampled candidate set (~2–3× the requested limit) using the quantized index, then rescores those against the original vectors and truncates to the limit. `quantile: 0.99` clips outliers when computing the int8 scale. *Alternative:* no rescore (faster, lower recall) — rejected as default; recall matters more than the marginal latency here.
- **In-place, idempotent rollout.** At startup, for an existing collection without quantization configured, call `update_collection` with the scalar config (background re-quantization). New collections get `QuantizationConfig` at creation. Guard with a collection-info check so repeated startups are no-ops. *Alternative:* drop + recreate + re-ingest — rejected; unnecessary and costly.
- **Config-toggled, default on.** `QdrantOptions.Quantization.Enabled` (default true) lets it be disabled without code changes; the rescore/oversampling/quantile are also configurable.

## Risks / Trade-offs

- **[Recall regression from quantization]** → Mitigation: rescoring against originals + oversampling; the validation step measures recall vs the float32 baseline on a fixed query set and gates adoption.
- **[Background re-quantization load on a live collection]** → Mitigation: `update_collection` re-quantizes asynchronously via the optimizer; run it at startup and tolerate a brief period where search still uses the prior index. Non-blocking.
- **[Rescoring needs original vectors present]** → they are retained by default; do not enable an on-disk-only/drop-originals mode in this change.

## Migration Plan

1. Ship the config + initializer change (quantization on new + `update_collection` for existing); default on.
2. On next startup, existing collections re-quantize in the background; verify via `GetCollectionInfoAsync` that `QuantizationConfig` is present.
3. Rollback: set `Quantization.Enabled=false` and `update_collection` to clear quantization (or leave it — rescoring makes results equivalent). No data loss either way.

## Open Questions

- Oversampling factor (2× vs 3×): resolve empirically in the validation step against recall/latency; the config default is 2.0.
