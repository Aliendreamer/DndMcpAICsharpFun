# Tasks — replace-docling-with-marker

## 1. Neutral renames (serena rename_symbol, compiler-verified)

- [ ] 1.1 Rename `IDoclingPdfConverter` → `IPdfStructureConverter` (file → `Features/Ingestion/Pdf/IPdfStructureConverter.cs`)
- [ ] 1.2 Rename `DoclingDocument` → `PdfStructureDocument`, `DoclingItem` → `PdfStructureItem` (file → `PdfStructureDocument.cs`)
- [ ] 1.3 Rename `DoclingDiskCache` → `PdfConversionDiskCache` (file → `PdfConversionDiskCache.cs`)
- [ ] 1.4 Rename `EntityExtractionOptions.DoclingCacheDirectory` → `ConversionCacheDirectory`; update `Config/appsettings*.json` keys
- [ ] 1.5 Build solution — zero errors

## 2. HeadingDespacer (TDD)

- [ ] 2.1 Write failing tests `DndMcpAICsharpFun.Tests/Entities/Ingestion/HeadingDespacerTests.cs` using the real garble corpus: `ABER R ATIONS`→`ABERRATIONS`, `H U MANOIDS`→`HUMANOIDS`, `OPTIONAL C LASS FEATURES`→`OPTIONAL CLASS FEATURES`, `TH E WAR R IOR`→`THE WARRIOR`, `B EASTS`→`BEASTS`, `OOZ ES`→`OOZES`; preserved: `PATH OF THE BEAST`, `D4 ORIGIN`, `Animating Performance`, `BARD`
- [ ] 2.2 Implement `Features/Ingestion/Pdf/HeadingDespacer.cs` per design decision 3 (conservative whitelist merge)
- [ ] 2.3 Tests green; commit

## 3. Production MarkerPdfConverter (TDD)

- [ ] 3.1 Create `Infrastructure/Marker/MarkerOptions.cs` (`Url`, `PollIntervalSeconds` = 15, `ConversionTimeoutMinutes` = 240) with validation; bind `"Marker"` section; add to `Config/appsettings.json` (`http://marker:5002`) and `appsettings.Development.json` (`http://localhost:5002`)
- [ ] 3.2 Write failing tests for the JSON mapping (`MarkerPdfConverterTests`): heading mapping with level+page, dice-caption demotion (`d4 Desired Offering` → text item), PageHeader/Footer/Picture/Figure skipped, despacer applied to headings, nested Group recursion
- [ ] 3.3 Rework `Features/Ingestion/Pdf/MarkerPdfConverter.cs`: `IHttpClientFactory` injection, `MarkerOptions`, container path derived from `Ingestion:BooksPath` + file name, bounded polling loop honoring `ConversionTimeoutMinutes`, failure surfaces marker error; keep `FromMarkerJson` static mapping (now with caption demotion + despacer)
- [ ] 3.4 `PdfConversionDiskCache`: cache path becomes `<hash>.marker.json`; legacy `<hash>.json` ignored; unit test both scenarios
- [ ] 3.5 Create `Infrastructure/Marker/MarkerHealthCheck.cs` (GET `/health`, healthy when `models_loaded`); register replacing `DoclingHealthCheck`
- [ ] 3.6 DI: register `MarkerPdfConverter` as the `IPdfStructureConverter`, wrapped by `PdfConversionDiskCache`; remove docling registrations
- [ ] 3.7 Tests green; commit

## 4. Docling removal (clean up old code)

- [ ] 4.1 Delete `Features/Ingestion/Pdf/DoclingPdfConverter.cs`, `Infrastructure/Docling/DoclingHealthCheck.cs`, `Infrastructure/Docling/DoclingOptions.cs` (and the now-empty `Infrastructure/Docling/`)
- [ ] 4.2 Remove `Docling` config sections from `Config/appsettings*.json`; remove docling env vars from compose `app` service if present
- [ ] 4.3 Remove `docling` service from `docker-compose.yml`; `app.depends_on`: replace docling entry with `marker: condition: service_healthy`
- [ ] 4.4 Marker compose service: drop the spike comment, replace external `aidoctorassistant_marker-models` volume with project-owned `marker_models` named volume (no `external: true`)
- [ ] 4.5 Grep for remaining `[Dd]ocling` references in C#, compose, configs — only `data/docling-cache` directory name and historical openspec/docs may remain
- [ ] 4.6 Delete spike harness `DndMcpAICsharpFun.Tests/Spike/MarkerVsDoclingComparisonTests.cs` (reports in `data/spike/` stay)
- [ ] 4.7 Update `BlockIngestionOrchestrator` failure message to name the marker service per ingestion-pipeline delta spec; adjust/extend its tests
- [ ] 4.8 Build + full test suite green; commit

## 5. Docs + verification

- [ ] 5.1 Update `CLAUDE.md`: docling → marker (service table, architecture notes); no endpoint changes so `.http`/insomnia untouched — verify with `git diff --stat`
- [ ] 5.2 `docker compose config -q` validates; `docker compose up -d marker` healthy with the new project volume (models re-download once)
- [ ] 5.3 Full suite: `dotnet test` — all green
- [ ] 5.4 Commit

## 6. Operational (post-merge runbook, executed on request)

- [ ] 6.1 Re-convert + re-ingest blocks for the 3–4 registered books (`POST /admin/books/{id}/ingest-blocks`); expect ~2h/book marker conversion on first run, cached afterwards
- [ ] 6.2 Spot-check `dnd_blocks` section titles for despaced headings
