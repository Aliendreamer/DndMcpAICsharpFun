# Implementation Tasks

## 1. Bookmark reading

- [ ] 1.1 Update `Features/Ingestion/Pdf/PdfPigBookmarkReader.cs` so the walker recurses the entire bookmark tree (not just root + immediate children).
- [ ] 1.2 Register `IPdfBookmarkReader → PdfPigBookmarkReader` as a singleton in `Extensions/ServiceCollectionExtensions.cs`.
- [ ] 1.3 Update `DndMcpAICsharpFun.Tests/Ingestion/Pdf/PdfPigBookmarkReaderTests.cs` to cover the recursive walker (a fixture with nested children whose grandchildren must also appear in the result).

## 2. Bookmark → TOC mapping

- [ ] 2.1 Create `Features/Ingestion/Pdf/BookmarkTocMapper.cs` with a static `Map(IReadOnlyList<PdfBookmark>) → IReadOnlyList<TocSectionEntry>` method that assigns each entry a `ContentCategory` via case-insensitive keyword heuristic (titles → categories per the spec) and falls back to `ContentCategory.Rule` for unknown titles.
- [ ] 2.2 Add tests `DndMcpAICsharpFun.Tests/Ingestion/Pdf/BookmarkTocMapperTests.cs` covering: spell/monster/class/race/background/item/condition/god/plane/treasure/encounter/trap/feat/lore matches, the `Rule` fallback, case-insensitivity, and that the input order is preserved.

## 3. Orchestrator refactor

- [ ] 3.1 Modify `IngestionOrchestrator` constructor: drop the `ITocMapExtractor tocMapExtractor` parameter, add `IPdfBookmarkReader bookmarkReader`.
- [ ] 3.2 Replace the TOC-page → LLM call inside `ExtractBookAsync` with: read bookmarks → if empty, mark record `Failed` with message "PDF has no embedded bookmarks; bookmark-driven extraction requires them." and return; else run through `BookmarkTocMapper.Map(...)` and feed into the existing `TocCategoryMap`.
- [ ] 3.3 Apply the same change to `ExtractSinglePageAsync` (single-page debug path): read bookmarks → if empty, return entities = []; else build the map and proceed.
- [ ] 3.4 Remove every reference to `record.TocPage` from `IngestionOrchestrator` (including the early-return null guard and the "TOC page missing" log).
- [ ] 3.5 Update `DndMcpAICsharpFun.Tests/Ingestion/IngestionOrchestratorTests.cs` to substitute `IPdfBookmarkReader` instead of `ITocMapExtractor`. Cover: bookmarks present → entities extracted; bookmarks empty → record marked Failed; nested bookmarks produce correct page ranges through `TocCategoryMap`.

## 4. Registration API change

- [ ] 4.1 Remove the `tocPage` parsing block from `Features/Admin/BooksAdminEndpoints.cs::RegisterBook` (the `case "tocPage"` branch in the multipart switch and the `if (tocPage is null)` validation).
- [ ] 4.2 Drop `TocPage = tocPage.Value` from the `IngestionRecord` initializer.
- [ ] 4.3 Remove the `int TocPage` parameter from `RegisterBookRequest`.
- [ ] 4.4 Update `DndMcpAICsharpFun.Tests/Admin/BooksAdminEndpointsTests.cs`: delete `RegisterBook_MissingTocPage_Returns400`, remove the `tocPage` form parts from every other register test, and adjust the `Arg.Is<IngestionRecord>(r => r.TocPage == 3)` assertion to drop the TocPage check.

## 5. Database / EF migration

- [ ] 5.1 Drop the `TocPage` property from `Infrastructure/Sqlite/IngestionRecord.cs`.
- [ ] 5.2 Add a new EF migration `Migrations/<timestamp>_RemoveTocPageFromIngestionRecord.cs` whose `Up` calls `DropColumn("TocPage", "IngestionRecords")` and whose `Down` re-adds it as a nullable int column. Update `IngestionDbContextModelSnapshot.cs` to remove the `TocPage` property entry.

## 6. Delete the LLM TOC path

- [ ] 6.1 Delete `Features/Ingestion/Extraction/ITocMapExtractor.cs` (interface and `TocMapDebugResult` record).
- [ ] 6.2 Delete `Features/Ingestion/Extraction/OllamaTocMapExtractor.cs` (implementation + system prompt + log messages).
- [ ] 6.3 Delete `DndMcpAICsharpFun.Tests/Ingestion/Extraction/OllamaTocMapExtractorTests.cs`.
- [ ] 6.4 Remove `services.AddSingleton<ITocMapExtractor, OllamaTocMapExtractor>()` from `Extensions/ServiceCollectionExtensions.cs`.
- [ ] 6.5 Remove the `MapPost(/MapGet) "/books/{id:int}/debug-toc"` registration and the `DebugToc` handler from `Features/Admin/BooksAdminEndpoints.cs`.

## 7. Documentation / HTTP file

- [ ] 7.1 Update the register block in `DndMcpAICsharpFun.http` to drop the `tocPage` part.
- [ ] 7.2 Remove the "Debug TOC extraction" example from `DndMcpAICsharpFun.http`.
- [ ] 7.3 Confirm no other doc (README, CLAUDE.md, scripts) references `tocPage` or `debug-toc`; if so, update them.

## 8. Verification

- [ ] 8.1 Run `dotnet build` — expect zero errors and zero new warnings.
- [ ] 8.2 Run `dotnet test` — expect all tests to pass with no skipped tests beyond pre-existing skips.
- [ ] 8.3 Manual smoke test against a real bookmarked PHB PDF: register the book, kick off `/extract`, and inspect the resulting page JSON files for non-empty `entities` arrays with sensible `section_title` / `section_start` / `section_end` values.
- [ ] 8.4 Manual sanity check: `openspec status --change bookmark-driven-toc-extraction` shows all four artifacts done.
