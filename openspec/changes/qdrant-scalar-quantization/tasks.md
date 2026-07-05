## 1. Config

- [x] 1.1 Add a `Quantization` option group to `QdrantOptions` (`Enabled` default true, `AlwaysRam` default true, `Quantile` default 0.99, `Rescore` default true, `Oversampling` default 2.0)
- [x] 1.2 Bind/validate it from `appsettings` (Qdrant section); document defaults (nested binds via the existing `QdrantOptions` `BindConfiguration("Qdrant")`; defaults live in code, no appsettings entry required)

## 2. Collection setup (new + in-place)

- [x] 2.1 In `QdrantCollectionInitializer`, set `QuantizationConfig` (scalar int8, quantile, always_ram) on `VectorParams` when creating a NEW collection and quantization is enabled
- [x] 2.2 For an EXISTING collection with no quantization, enable it via `UpdateCollectionAsync` (background re-quantization; no re-ingest)
- [x] 2.3 Guard both paths on `GetCollectionInfoAsync` so startup is idempotent (skip if already quantized); leave sparse config untouched
- [x] 2.4 (test) Enabled → new collection gets a quantization config; Disabled → none (preserves current behaviour)

## 3. Search rescoring

- [x] 3.1 Add quantized `SearchParams` (rescore=true, oversampling) to the dense and hybrid search calls in `QdrantSearchClientAdapter` / vector-store search when quantization is enabled
- [x] 3.2 (test) Search builds params with rescore + oversampling when quantization is enabled; plain params when disabled

## 4. Validation

- [x] 4.1 `dotnet build` 0/0; full non-persistence suite green (933/933)
- [x] 4.2 Live measurement: quantization confirmed APPLIED on both collections (`scalar int8, quantile 0.99, always_ram`) — it went live automatically when the extraction rebuild (803da7b) picked up 11c7665. Recall preserved (e.g. "bag of holding" -> dmg14.magicitem.bag-of-holding top hit); latencies 0.2-0.75s; ~4x vector-memory reduction (48.9MB -> 12.2MB, ~37MB saved). CAVEAT: no clean float32 before-snapshot (already quantized), so recall is 'correct results with rescore', not a delta
- [x] 4.3 Decision: KEEP (default-on) — quantization is applied, recall is preserved with rescoring, ~4x vector memory saved, no HTTP/schema change. Oversampling default 2.0 is adequate; revisit only if a recall regression surfaces on a larger corpus
