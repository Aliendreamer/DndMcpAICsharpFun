## 1. EF Core reliability

- [x] 1.1 Wrap campaign deletion in a transaction or DB cascade (NET-01, `Features/Campaigns/CampaignRepository.cs:41`)
- [x] 1.2 Wrap hero deletion in a transaction (NET-03, `Features/Campaigns/HeroRepository.cs:87`)
- [x] 1.3 Replace the per-hero latest-snapshot loop with one grouped query (NET-02, `HeroRepository.cs:22`)
- [x] 1.4 Decide and apply `jsonb`+GIN vs `text` for JSON columns (NET-12, `Infrastructure/Persistence/AppDbContext.cs:62`)

## 2. Async + resource hygiene

- [x] 2.1 Propagate `CancellationToken` to block deletion (NET-04, `Features/Ingestion/BlockIngestionOrchestrator.cs:55`)
- [ ] 2.2 Make `IPdfBlockExtractor` async; await conversion instead of blocking (NET-06, `Pdf/StructureBlockExtractor.cs:11`)
- [x] 2.3 Batch the projector's `SaveChangesAsync` (NET-07, `Features/Resolution/StructuredFactProjector.cs:24`)
- [x] 2.4 Evict/dispose per-key semaphores (NET-05, `EntityExtraction/CanonicalJsonWriter.cs:9`)
- [x] 2.5 Distinguish + log web-search failures with the exception (NET-11, `Features/Search/SearXNGClient.cs:34`)

## 3. Data-path logic bugs

- [x] 3.1 Guard slug-collision canonical deletion (COR-18, `Features/Ingestion/BookDeletionService.cs:33`)
- [x] 3.2 Propagate table/choice-set removals on re-projection (COR-21, `StructuredFactProjector.cs:30`) — tables by SourceBook, choice-sets by canonical-id slug prefix; regression test added
- [x] 3.3 Render ability lines only when values present (COR-13, `CanonicalText/MonsterCanonicalTextRenderer.cs:84`)
- [x] 3.4 Throw descriptive errors on malformed MinerU responses (COR-19, `Pdf/MinerUPdfConverter.cs:56`)
- [x] 3.5 Attribute computed `saveDC` provenance correctly (COR-20, `Features/Resolution/CharacterResolutionService.cs:152`) — computed value now carries null provenance

## 4. Test hygiene

- [x] 4.1 Remove dead `Build()` helper (COR-01, `.../Admin/AdminApiKeyMiddlewareTests.cs:10`)
- [x] 4.2 Use shared `TestPaths` helper instead of `../../../../` (COR-03, `.../Admin/BooksAdminEndpointsTests.cs:33`)
- [x] 4.3 Isolate temp dirs with try/finally cleanup (COR-04, COR-07, `.../BooksAdminEndpointsTests.cs:80`, `.../Chat/DndChatServiceTests.cs:30`) — per-instance `_booksDir` + `Dispose`; 6 scattered cleanup loops removed
- [x] 4.4 Signal async completion instead of `Task.Delay(150)` (COR-08, `.../Ingestion/IngestionQueueWorkerTests.cs:25`)
- [x] 4.5 Tighten weak `Contain("15")` assertion to `DC 15` (COR-09, `.../Mcp/ResolveCharacterFeatureToolTests.cs:117`)

## 5. Verify + close

- [ ] 5.1 `dotnet build` + `dotnet test` green (Docker up for persistence tests)
- [ ] 5.2 Confirm each finding (NET-01..07/11/12, COR-01/03/04/07/08/09/13/18/19/20/21) is addressed
