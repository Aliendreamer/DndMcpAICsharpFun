## 1. De-duplicate shared logic

- [x] 1.1 One book-slug helper, replace ~14 call sites (SIM-07, `.../Entities/EntityIngestionOrchestrator.cs:46`) — `EntityIdSlug.BookSlug`; one site inside EntityExtractionOrchestrator deferred to STR-09 split
- [x] 1.2 Single `FivetoolsMapperRegistry` (SIM-14, STR-10, `FivetoolsIngestionService.cs:17`, `FivetoolsRecordIndex.cs:17`)
- [x] 1.3 Single `Edition2024Sources` source (SIM-15, STR-11, `FivetoolsMapperBase.cs:9`, `SpellBackfillService.cs:28`)
- [x] 1.4 Shared renderer helpers + feature-entry extraction (SIM-05, SIM-06, `SimpleEntityRenderers.cs:14`, `SubclassCanonicalTextRenderer.cs:30`) — Monster AlignMap kept local (genuine divergence); ExtractFeatureEntry parameterized on minParts
- [x] 1.5 Single sidecar-file writer + shared `IsSidecar` (SIM-13, SIM-02, `ExtractionDeclinedFile.cs:21`, `NeedsReviewService.cs:48`) — SIM-02 also fixed a real bug (inline list omitted `.declined.json`)
- [x] 1.6 Heading-promotion local function (SIM-16, `MinerUPdfConverter.cs:88`)
- [x] 1.7 One enrich+merge helper (SIM-08, STR-08, `EntityIngestionOrchestrator.cs:189,68`) — three narrower helpers (BuildFivetoolsIndexAsync/MergeEnrichment/WithRenderedText); divergent flow kept inline
- [x] 1.8 One bounded fuzzy-match scan (SIM-12, `EntityNameMatcher.cs:56`)

## 2. Remove dead code

- [x] 2.1 Delete `CanonicalJson.WriteAsync` + `ReadOptions` (SIM-10, SIM-11, `CanonicalJson.cs:19,27`)
- [x] 2.2 Remove `EntityIngestionResult.Enriched` alias (SIM-09, `IEntityIngestionOrchestrator.cs:30`)
- [x] 2.3 Drop unused `IEntityCanonicalTextRenderer<TFields>` abstraction (SIM-04) — removed from 4 renderers + interface deleted
- [x] 2.4 Clean stray blank-line/comment gaps (SIM-01, SIM-03)

## 3. Split the god file

- [ ] 3.1 Decompose `EntityExtractionOrchestrator` into candidate pipeline + extraction runner + thin orchestrator; assert identical output before/after (STR-09, `EntityExtractionOrchestrator.cs:12`)

## 4. Layering conventions

- [ ] 4.1 Move EF attributes off Domain types to Fluent config (STR-01, STR-02, `Domain/Hero.cs`, `Domain/IngestionRecord.cs`)
- [ ] 4.2 Remove unused Infrastructure `using` in feature interface (STR-12, `IIngestionTracker.cs:1`)
- [ ] 4.3 Adopt `IDbContextFactory` (or document exception) in `IngestionTracker` (STR-13, `IngestionTracker.cs:8`)
- [ ] 4.4 Standardize Admin endpoint mapping; extract multipart parsing + shared options (STR-03, STR-04, STR-05, STR-06)
- [ ] 4.5 Move inline option-binding + reranker DI into per-feature extensions (STR-16, `Program.cs:30`)
- [ ] 4.6 Document (ADR/README) the Ingestion→Entities shared-kernel relationship, or relocate the shared loaders under Ingestion (STR-07, `.../Entities/EntityIngestionOrchestrator.cs:1`)

## 5. Verify + close

- [ ] 5.1 `dotnet build` + `dotnet test` green — behaviour unchanged
- [ ] 5.2 Confirm each finding (SIM-01..16, STR-01..13/16) is addressed
