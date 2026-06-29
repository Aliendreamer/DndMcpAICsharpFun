## 1. MinerU conversion service (the key unknown)

- [ ] 1.1 Source or build a MinerU GPU service image (MinerU `mineru-api` FastAPI + torch + pipeline/OCR models, GPU access). Spike the image first: confirm `pipeline`+`ocr` runs in-container on a sample PDF and returns the `content_list`. If no clean image exists, build a thin Dockerfile around the `mineru` CLI/api.
- [ ] 1.2 Add the `mineru` service to `docker-compose.yml` (GPU reservation, models volume, on the shared network). Document the one-time model download.

## 2. MinerUPdfConverter → service (replace the spike file-read)

- [ ] 2.1 Extend `MinerUOptions` with `ServiceUrl`, `Backend` (default `pipeline`), `Method` (default `ocr`), `ConversionTimeoutMinutes`; keep `Enabled`. Remove the spike `OutputDirectory` file-read path.
- [ ] 2.2 Rewrite `MinerUPdfConverter.ConvertAsync` to call the MinerU service and consume its `content_list`, then map blocks → items (TDD with a stubbed service: text_level→section_header, text→text, others dropped). **API contract (verified against the live `mineru:8000`):** `POST /file_parse` (multipart/form-data), fields: `files`=<pdf> (required), `backend=pipeline`, `parse_method=ocr`, `return_content_list=true` (default false — MUST set), `return_md=true` optional. Response JSON carries the `content_list` array (`{type,text,text_level,page_idx}` per block — same shape the spike reads). For a ~300-page book the sync call is long, so prefer the async flow: `POST /tasks` → poll `GET /tasks/{id}` → `GET /tasks/{id}/result` (mirror `MarkerPdfConverter`'s submit/poll). `GET /health` for the health check.
- [ ] 2.3 Keep the spell-chapter splitter unchanged (the `Casting Time:` anchor + name extraction) — covered by existing tests.

## 3. Conversion caching

- [ ] 3.1 Add a MinerU conversion disk cache keyed by PDF content hash (mirror `PdfConversionDiskCache`), wrapping `MinerUPdfConverter` (TDD: second convert of the same PDF hits the cache, no service call).

## 4. DI default + Marker fallback

- [ ] 4.1 In `ServiceCollectionExtensions`, register MinerU (service converter + cache) as the default `IPdfStructureConverter` when `MinerU:Enabled` (default true); Marker disk-cache when false (TDD via options).
- [ ] 4.2 Retire the spike enablement: remove the gitignored `docker-compose.override.yml` reliance; MinerU config lives in `docker-compose.yml`.

## 5. Build, docs

- [ ] 5.1 `dotnet build` 0 warnings; full non-persistence suite green.
- [ ] 5.2 Update CLAUDE.md (parser is MinerU `pipeline`+`ocr`; Marker is the fallback; the spell-splitter; OCR cost/caching). Lint markdown. No `.http`/insomnia change.

## 6. Live validation (acceptance gate)

- [ ] 6.1 Bring up the MinerU service; re-convert + re-extract PHB through it (force).
- [ ] 6.2 Confirm the spike numbers: 12/12 classes, 7/9 races, ~323/361 spells, zero noise, no manual CLI step; conversion cached (second run skips OCR).
- [ ] 6.3 Re-convert + re-extract MM and DMG; then ready to ingest the corrected canonical corpus-wide.
