# Implementation Tasks

## 1. Verify docling-serve image and API shape

- [ ] 1.1 Pull `quay.io/ds4sd/docling-serve-cpu:latest` locally and confirm it runs (`docker run --rm -p 5001:5001 quay.io/ds4sd/docling-serve-cpu:latest`).
- [ ] 1.2 Issue a manual conversion against the registered PHB to capture the actual JSON response shape: `curl -F "files=@PlayerHandbook2014.pdf" http://localhost:5001/v1alpha/convert/file > /tmp/docling-sample.json`.
- [ ] 1.3 Inspect the response: confirm there's a markdown body field and an items/elements list with type, text, and page_no per item. Identify the exact field names — they drive the C# DTO.
- [ ] 1.4 Pin the image to a specific tag once verified (look at registry tags; prefer a dated/versioned tag over `:latest`).

## 2. Compose service

- [ ] 2.1 Add the `docling` service to `docker-compose.yml`:
    ```yaml
    docling:
      image: quay.io/ds4sd/docling-serve-cpu:<pinned-tag>
      ports:
        - "5001:5001"
      healthcheck:
        test: ["CMD-SHELL", "wget -qO- http://localhost:5001/health || exit 1"]
        interval: 15s
        timeout: 5s
        retries: 5
        start_period: 90s
      networks:
        - dnd_net
      restart: unless-stopped
    ```
- [ ] 2.2 Same change in `docker-compose.prod.yml`.
- [ ] 2.3 Add `docling: { condition: service_healthy }` under `app.depends_on` in both compose files.

## 3. Configuration and DTOs

- [ ] 3.1 Add `Infrastructure/Docling/DoclingOptions.cs`:
    ```csharp
    public sealed class DoclingOptions
    {
        public string BaseUrl { get; set; } = "http://docling:5001";
        public int RequestTimeoutSeconds { get; set; } = 600;
    }
    ```
- [ ] 3.2 Bind `Docling` section in `Program.cs` (or wherever the other options are bound).
- [ ] 3.3 Add the `Docling` section to `Config/appsettings.json` with the defaults.
- [ ] 3.4 Define `Features/Ingestion/Pdf/DoclingDocument.cs`:
    ```csharp
    public sealed record DoclingDocument(string Markdown, IReadOnlyList<DoclingItem> Items);
    public sealed record DoclingItem(string Type, string Text, int PageNumber, int? Level);
    ```

## 4. HTTP client

- [ ] 4.1 Add `Features/Ingestion/Pdf/IDoclingPdfConverter.cs` with `Task<DoclingDocument> ConvertAsync(string filePath, CancellationToken ct = default)`.
- [ ] 4.2 Implement `Features/Ingestion/Pdf/DoclingPdfConverter.cs`:
    - Constructor injects `IOptions<DoclingOptions>` and `ILogger<DoclingPdfConverter>`.
    - Owns a single `HttpClient` with `BaseAddress` and `Timeout` from options.
    - `ConvertAsync` reads the PDF, builds a `MultipartFormDataContent` with the file, POSTs to `/v1alpha/convert/file`, deserialises the JSON response into `DoclingDocument`.
    - Throws `InvalidOperationException` with status code + body on non-2xx.
- [ ] 4.3 Register `IDoclingPdfConverter -> DoclingPdfConverter` (Singleton) in `ServiceCollectionExtensions.AddIngestionPipeline`.

## 5. Block extractor adapter

- [ ] 5.1 Add `Features/Ingestion/Pdf/DoclingBlockExtractor.cs` implementing `IPdfBlockExtractor`:
    - Constructor injects `IDoclingPdfConverter` and `ILogger<DoclingBlockExtractor>`.
    - `ExtractBlocks(filePath)` is `IEnumerable<PdfBlock>`. Calls `ConvertAsync(...).GetAwaiter().GetResult()` (acceptable here — the existing `IPdfBlockExtractor.ExtractBlocks` is sync; if the orchestrator is later refactored to async, the interface follows).
    - For each `DoclingItem`, skip whitespace-only text. Yield a `PdfBlock(text, pageNumber, perPageOrder++, default(PdfRectangle))`. Bounding box is `default` since Docling does not return one in the same form; payload doesn't currently use it.
- [ ] 5.2 Register `DoclingBlockExtractor` (Singleton).

## 6. Factory: pick the right extractor

- [ ] 6.1 Replace the existing `services.AddSingleton<IPdfBlockExtractor, PdfPigBlockExtractor>()` with a factory that reads `IngestionOptions.BlockSegmenter` and resolves either `PdfPigBlockExtractor` (default for `"docstrum"` and `"xycut"`) or `DoclingBlockExtractor` (for `"docling"`):
    ```csharp
    services.AddSingleton<IPdfBlockExtractor>(sp =>
    {
        var mode = sp.GetRequiredService<IOptions<IngestionOptions>>().Value.BlockSegmenter ?? "docstrum";
        return string.Equals(mode, "docling", StringComparison.OrdinalIgnoreCase)
            ? sp.GetRequiredService<DoclingBlockExtractor>()
            : sp.GetRequiredService<PdfPigBlockExtractor>();
    });
    ```
- [ ] 6.2 Register `PdfPigBlockExtractor` and `DoclingBlockExtractor` as concrete singletons so the factory can resolve either.

## 7. Health check

- [ ] 7.1 Add `Infrastructure/Docling/DoclingHealthCheck.cs`:
    - Implements `IHealthCheck`.
    - GETs `{BaseUrl}/health` with a short timeout (5 s).
    - Returns `Healthy` on 2xx, `Unhealthy` otherwise with the response status.
- [ ] 7.2 Register the check in `Program.cs` next to the existing Qdrant and Ollama checks: `.AddCheck<DoclingHealthCheck>("docling")`.

## 8. Error handling in the orchestrator

- [ ] 8.1 In `BlockIngestionOrchestrator.IngestBlocksAsync`, wrap the `blockExtractor.ExtractBlocks(...)` call so a thrown `HttpRequestException` / `InvalidOperationException` from the Docling path is surfaced as the record's `Failed` error message. The existing top-level `catch (Exception ex)` already handles this — verify the message is informative; refine the log if not.

## 9. Tests

- [ ] 9.1 `DoclingPdfConverterTests.cs`:
    - Use a `HttpMessageHandler` test double.
    - Assert: 2xx with valid JSON returns parsed `DoclingDocument`; 5xx throws `InvalidOperationException`; timeout propagates `TaskCanceledException`.
- [ ] 9.2 `DoclingBlockExtractorTests.cs`:
    - Use `Substitute.For<IDoclingPdfConverter>()` returning canned `DoclingDocument` instances.
    - Assert: items map to PdfBlocks with correct page numbers, per-page Order index resets per page, whitespace items are skipped.
- [ ] 9.3 `BlockExtractorFactoryTests.cs` (or extend existing DI test):
    - Build a service provider with `BlockSegmenter = "docstrum"` → resolved `IPdfBlockExtractor` is `PdfPigBlockExtractor`.
    - Same with `"xycut"` → `PdfPigBlockExtractor` (different segmenter inside, same outer type).
    - `"docling"` → `DoclingBlockExtractor`.
    - Invalid value → `PdfPigBlockExtractor`.

## 10. Documentation

- [ ] 10.1 Update `DndMcpAICsharpFun.http`: extend the existing `BlockSegmenter` comment to include `"docling"` as a valid value.
- [ ] 10.2 Update `CLAUDE.md` (Observability section) to include docling-serve URL on `localhost:5001/docs` if the OpenAPI page is exposed there.

## 11. Verification

- [ ] 11.1 `dotnet build` — zero errors.
- [ ] 11.2 `dotnet test` — all tests pass.
- [ ] 11.3 `docker compose up -d --build` — confirm logs show docling becoming healthy before app starts. Confirm `/ready` shows `docling: Healthy`.
- [ ] 11.4 Manual smoke test:
    1. With `Ingestion__BlockSegmenter=docling` set in compose, `docker compose up -d app`.
    2. Re-ingest the registered PHB: `POST /admin/books/1/ingest-blocks`.
    3. Watch logs: confirm `Block batch N/M upserted` lines from the orchestrator; the time per batch will be longer than PdfPig (CPU layout analysis runs).
    4. After completion, `GET /collections/dnd_blocks` from Qdrant — note point count.
    5. Run the same probe queries as the xycut experiment:
        - `q=fireball`, `q=goblin`, `q=how do gods work in dnd`, `q=how does grappling work`, `q=what is a bard`.
    6. Compare top-3 results against the docstrum baseline. Specifically check whether the multi-column word interleaving is gone — this is the whole point.
- [ ] 11.5 Record the comparison in a `results.md` next to `tasks.md`. Decide:
    - Clear win → flip default in `appsettings.json` to `"docling"` in a follow-up commit.
    - No improvement → revert by removing the docling service and the block-extractor factory's docling branch. Open a `markerpdf-pdf-extraction` proposal.
    - Mixed → leave the knob in place at default `"docstrum"`, document the use case where `"docling"` helps and where it doesn't.
- [ ] 11.6 `openspec status --change docling-pdf-extraction` shows all artifacts done.
