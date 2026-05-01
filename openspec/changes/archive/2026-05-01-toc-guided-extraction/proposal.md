## Why

The LLM extractor currently runs all 8 category passes on every page of a book, causing both wasted compute (53s per pass on pages with no matching content) and silent hallucination (the LLM fills the template with generic text when no real entity exists). Using the PDF's embedded bookmark outline to pre-determine which content category belongs on each page range eliminates irrelevant passes before they run.

## What Changes

- New pre-extraction step reads the PDF bookmark outline (via PdfPig) at the start of `/extract` — transparent to the user
- LLM is called once per book to map bookmark chapter titles to `ContentCategory` page ranges (instead of per page)
- The extractor only runs the relevant category pass per page (or skips entirely for intro/appendix pages)
- New `POST /admin/books/{id}/cancel-extract` endpoint stops a running extraction, deletes partial JSON files, and resets book status to `Pending`

## Capabilities

### New Capabilities

- `toc-category-map`: Reads the PDF bookmark outline and uses the LLM to produce a page-range → ContentCategory map, stored ephemerally per extraction run
- `extraction-cancellation`: Endpoint to cancel an in-progress extraction for a book, with cleanup and status reset to Pending

### Modified Capabilities

- `ingestion-pipeline`: Extraction step gains TOC-guided category filtering — each page now runs at most one extractor pass instead of all 8

## Impact

- `Features/Ingestion/Extraction/` — new TOC reader + LLM classification step, changes to per-page extractor dispatch
- `Features/Admin/BooksAdminEndpoints.cs` — new cancel endpoint
- `Features/Ingestion/IngestionQueueWorker.cs` — per-job CancellationTokenSource tracking
- `Features/Ingestion/IngestionOrchestrator.cs` — cancel support + cleanup logic
- No new NuGet dependencies (PdfPig already present, Ollama client already present)
- No breaking changes to existing API contracts
