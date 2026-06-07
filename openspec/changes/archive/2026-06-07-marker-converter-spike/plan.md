# Marker Converter Spike — Plan

> Spike plan (throwaway code, report is the deliverable). Executed inline — long-running docker orchestration makes subagent dispatch impractical. Runs in the main tree: needs git-ignored `books/` + `data/docling-cache`.

## Tasks

- [x] 1. `docker/marker/Dockerfile` — byte-identical to aidoctor's (reuses its docker layer cache); `docker/marker/app.py` — copied, stripped of LLM mode + save-to-disk, `output_format: "json"`, `/convert-by-path` + `/status/{job_id}` + `/result/{job_id}` (job returns parsed JSON, kept in memory).
- [x] 2. `marker` service in `docker-compose.yml` — port 5002, `./books:/books:ro`, external volume `aidoctorassistant_marker-models:/root/.cache/datalab` (skips model download), GPU reservation, `dnd_net`. Start it (`docker compose up -d marker`) immediately — long pole.
- [x] 3. `Features/Ingestion/Pdf/MarkerPdfConverter.cs` — spike-grade `IDoclingPdfConverter`: POST `/convert-by-path`, poll `/status`, GET `/result`, map Marker JSON block tree → `DoclingDocument.Items` (`SectionHeader` → heading item with depth-derived level; text-bearing blocks → text items; page from `/page/N` ids or page grouping). Not DI-registered.
- [x] 4. `DndMcpAICsharpFun.Tests/Spike/MarkerVsDoclingComparisonTests.cs` — `[Fact]` guarded by env var `RUN_SPIKE=1` (skipped otherwise): for each converter → items → replicated `BuildScannerInputs` → bookmark `TocCategoryMap` (same as orchestrator) → `EntityCandidateScanner` → `ExtractionNeedsReview.Derive(name, null)` → write `data/spike/marker-vs-docling.md` (counts per type, flagged rates, 20 worst names side-by-side, verdict numbers).
- [x] 5. Kick Marker conversion of `books/4e8f1fe851c34c7db1e8a0d8e1bff02d.pdf` (Tasha's), poll to completion.
- [x] 6. Run harness, generate + present report. STOP for discussion.

## Key references

- Bookmark→TOC: `EntityExtractionOrchestrator.ExtractAsync` lines ~68–73 (`bookmarks` reader → `TocSectionEntry` list → `new TocCategoryMap(entries)`).
- Scanner input builder to replicate: `EntityExtractionOrchestrator.BuildScannerInputs` (private, ~line 535).
- Heuristic: `Features/Ingestion/EntityExtraction/ExtractionNeedsReview.cs`.
- Docling side: `DoclingPdfConverter` + `DoclingDiskCache` (cache hit for Tasha's).
