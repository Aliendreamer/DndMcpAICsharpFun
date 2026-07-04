## 1. Config

- [ ] 1.1 Add a `Quantization` option group to `QdrantOptions` (`Enabled` default true, `AlwaysRam` default true, `Quantile` default 0.99, `Rescore` default true, `Oversampling` default 2.0)
- [ ] 1.2 Bind/validate it from `appsettings` (Qdrant section); document defaults

## 2. Collection setup (new + in-place)

- [ ] 2.1 In `QdrantCollectionInitializer`, set `QuantizationConfig` (scalar int8, quantile, always_ram) on `VectorParams` when creating a NEW collection and quantization is enabled
- [ ] 2.2 For an EXISTING collection with no quantization, enable it via `UpdateCollectionAsync` (background re-quantization; no re-ingest)
- [ ] 2.3 Guard both paths on `GetCollectionInfoAsync` so startup is idempotent (skip if already quantized); leave sparse config untouched
- [ ] 2.4 (test) Enabled → new collection gets a quantization config; Disabled → none (preserves current behaviour)

## 3. Search rescoring

- [ ] 3.1 Add quantized `SearchParams` (rescore=true, oversampling) to the dense and hybrid search calls in `QdrantSearchClientAdapter` / vector-store search when quantization is enabled
- [ ] 3.2 (test) Search builds params with rescore + oversampling when quantization is enabled; plain params when disabled

## 4. Validation

- [ ] 4.1 `dotnet build` 0/0; full non-persistence suite green (persistence/Qdrant tests still pass)
- [ ] 4.2 Live measurement: capture Qdrant collection memory before/after (`/collections/{name}` info) and run a fixed query set to compare recall + latency (quantized+rescore vs float32 baseline)
- [ ] 4.3 Record the result vs the accepted recall tolerance and decide keep/adjust-oversampling/disable (design.md); no HTTP/schema change so `.http`/`.insomnia` unchanged
