# Implementation Tasks

## 1. Verify the segmenter exists in our PdfPig version

- [ ] 1.1 Grep `RecursiveXYCut` across the project to confirm the type is reachable. If absent, abandon this change and start a `docling-pdf-extraction` proposal instead.

## 2. Add the configuration knob

- [ ] 2.1 Add `public string BlockSegmenter { get; set; } = "docstrum";` to `Infrastructure/Sqlite/IngestionOptions.cs`.
- [ ] 2.2 Add `"BlockSegmenter": "docstrum"` under the `Ingestion` section of `Config/appsettings.json` for discoverability.

## 3. Wire the segmenter selection

- [ ] 3.1 Modify `Features/Ingestion/Pdf/PdfPigBlockExtractor.cs`:
    - Constructor takes `IOptions<IngestionOptions>` and `ILogger<PdfPigBlockExtractor>`.
    - Resolve segmenter once: `"xycut"` (case-insensitive) → `RecursiveXYCut.Instance`; `"docstrum"` → `DocstrumBoundingBoxes.Instance`; anything else → log warning, use `DocstrumBoundingBoxes.Instance`.
    - Replace the hardcoded `DocstrumBoundingBoxes.Instance.GetBlocks(words)` call with the resolved instance.
- [ ] 3.2 Add a `[LoggerMessage]` for the unknown-value warning.
- [ ] 3.3 If the existing DI registration is `AddSingleton<IPdfBlockExtractor, PdfPigBlockExtractor>()`, no DI change is needed (constructor injection picks up the new params automatically).

## 4. Tests

- [ ] 4.1 Update `DndMcpAICsharpFun.Tests/Ingestion/Pdf/PdfPigBlockExtractorTests.cs` to construct the SUT with `Options.Create(new IngestionOptions { BlockSegmenter = "..." })` and `NullLogger<PdfPigBlockExtractor>.Instance`.
- [ ] 4.2 Add 3 new test cases:
    - `Default_UsesDocstrum_ProducesBlocks` — instantiate with default options, run against the Helvetica fixture PDF, assert blocks come back.
    - `XyCutSelected_ProducesBlocks` — instantiate with `BlockSegmenter = "xycut"`, run against the same fixture, assert blocks come back. (We are not asserting different content, only that the path is wired and no exception is thrown.)
    - `InvalidValue_FallsBackToDocstrum_AndLogsWarning` — instantiate with `BlockSegmenter = "nonsense"`, capture logs via a test logger, assert at least one warning is emitted, assert blocks come back.

## 5. .http and docs

- [ ] 5.1 Add a one-line comment near the `### Admin Books — Ingest blocks ...` block in `DndMcpAICsharpFun.http` mentioning the `Ingestion:BlockSegmenter` knob and its valid values.

## 6. Verification

- [ ] 6.1 `dotnet build` — zero errors.
- [ ] 6.2 `dotnet test` — all tests pass; new count is ~109 (current 106 + 3 new cases).
- [ ] 6.3 Manual A/B against the registered PHB:
    1. With default config, re-ingest blocks: `POST /admin/books/1/ingest-blocks`. Note `dnd_blocks` point count.
    2. Set `Ingestion__BlockSegmenter=xycut` in `docker-compose.yml` `app.environment`, `docker compose up -d app`.
    3. Re-ingest. Note new point count.
    4. Run a fixed list of probe queries against both ingests:
        - `q=fireball` (named entity)
        - `q=how do gods work in dnd` (conceptual)
        - `q=how does grappling work` (rule)
        - `q=what is a bard` (class intro — current scrambling baseline)
        - `q=goblin` (monster)
    5. For each query, paste the top-3 results from each segmenter side by side and grade 0/1/2 per result for "is this a useful coherent answer".
    6. Decide:
        - If `xycut` clearly wins: change the default in `appsettings.json` to `"xycut"` in a follow-up commit.
        - If `xycut` clearly loses or ties: leave default at `"docstrum"`. Open a `docling-pdf-extraction` proposal as the next escalation.
        - If results are mixed (some queries better, some worse): document the trade-off, leave knob in place, escalate to Docling anyway since neither built-in segmenter is good enough.
- [ ] 6.4 Record the comparison results in a short note appended to this `tasks.md` (or a sibling `results.md`) before archiving the change.
