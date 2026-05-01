## 1. TOC Bookmark Reader

- [x] 1.1 Add `IPdfBookmarkReader` interface with a `ReadBookmarks(filePath)` method returning a list of `(Title, PageNumber)` tuples
- [x] 1.2 Implement `PdfPigBookmarkReader` using `PdfDocument.Bookmarks` from PdfPig
- [x] 1.3 Handle the no-bookmarks fallback — return empty list, caller logs warning

## 2. LLM TOC Classifier

- [x] 2.1 Add `ITocCategoryClassifier` interface with `ClassifyAsync(bookmarks)` returning `Dictionary<int, ContentCategory?>` (page → category, null = skip)
- [x] 2.2 Implement `OllamaTocCategoryClassifier` — send bookmark titles + page numbers to LLM, parse JSON response into page-range map
- [x] 2.3 Handle invalid/unparseable LLM response — log warning, return empty map (triggers all-categories fallback)

## 3. Extraction Dispatch Update

- [x] 3.1 Update `IngestionOrchestrator.ExtractBookAsync` to call bookmark reader and classifier before the page loop
- [x] 3.2 Pass the page→category map into the per-page dispatch — run only the mapped extractor pass (or skip if null)
- [x] 3.3 If map is empty (no bookmarks or classifier failed), preserve existing all-categories behaviour

## 4. Extraction Cancellation

- [x] 4.1 Add `IExtractionCancellationRegistry` interface with `Register(bookId, cts)`, `Cancel(bookId) → bool`, and `Unregister(bookId)` methods
- [x] 4.2 Implement `ExtractionCancellationRegistry` as a singleton with a thread-safe `Dictionary<int, CancellationTokenSource>`
- [x] 4.3 Update `IngestionQueueWorker` to register/unregister a linked CTS around `ExtractBookAsync` calls
- [x] 4.4 Update `IngestionOrchestrator.ExtractBookAsync` to catch `OperationCanceledException`, delete `extracted/{bookId}/` folder, and reset status to `Pending`
- [x] 4.5 Add `POST /admin/books/{id}/cancel-extract` endpoint — calls `IExtractionCancellationRegistry.Cancel(id)`, returns 200 or 404
- [x] 4.6 Update `DndMcpAICsharpFun.http` with the new cancel-extract example request

## 5. Service Registration

- [x] 5.1 Register `IPdfBookmarkReader`, `ITocCategoryClassifier`, and `IExtractionCancellationRegistry` in `ServiceCollectionExtensions`

## 6. Tests

- [x] 6.1 Unit test `PdfPigBookmarkReader` — verify it returns titles and page numbers from a known PDF outline
- [x] 6.2 Unit test `OllamaTocCategoryClassifier` — mock LLM response, verify correct page-range map output
- [x] 6.3 Unit test cancellation cleanup — verify `extracted/{bookId}/` is deleted and status reset to `Pending` on cancel
- [x] 6.4 Unit test cancel endpoint — 200 when extraction running, 404 when not
