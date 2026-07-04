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
- [ ] 4.2 Live measurement: capture Qdrant collection memory before/after (`/collections/{name}` info) and run a fixed query set to compare recall + latency (quantized+rescore vs float32 baseline) — DEFERRED: needs an app-image rebuild, which would kill the in-flight DMG extraction; run after it completes
- [ ] 4.3 Record the result vs the accepted recall tolerance and decide keep/adjust-oversampling/disable (design.md); no HTTP/schema change so `.http`/`.insomnia` unchanged — DEFERRED with 4.2
