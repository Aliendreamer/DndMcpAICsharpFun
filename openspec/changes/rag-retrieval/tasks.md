## 1. Models & Options

- [x] 1.1 Create `Features/Retrieval/RetrievalQuery.cs` record with: `QueryText`, `Version?`, `Category?`, `SourceBook?`, `EntityName?`, `TopK` (default 5)
- [x] 1.2 Create `Features/Retrieval/RetrievalResult.cs` record with: `Text`, `Metadata` (ChunkMetadata), `Score`
- [x] 1.3 Create `Features/Retrieval/RetrievalDiagnosticResult.cs` record extending `RetrievalResult` with `PointId`
- [x] 1.4 Create `Features/Retrieval/RetrievalOptions.cs` with `ScoreThreshold` (default 0.5f), `MaxTopK` (default 20)
- [x] 1.5 Register `IOptions<RetrievalOptions>` in `Program.cs`
- [x] 1.6 `Retrieval` section already in `appsettings.json` with default values

## 2. Retrieval Service

- [x] 2.1 Create `Features/Retrieval/IRagRetrievalService.cs` with `SearchAsync` and `SearchDiagnosticAsync`
- [x] 2.2 Create `Features/Retrieval/RagRetrievalService.cs` implementing `IRagRetrievalService`
- [x] 2.3 Implement query embedding: call `IEmbeddingService.EmbedAsync([query.QueryText])`, take first result
- [x] 2.4 Implement Qdrant filter builder: iterate non-null query filter fields, build `Filter` with `Must` conditions
- [x] 2.5 Call `QdrantClient.SearchAsync` with vector, filter, limit = `Min(query.TopK, MaxTopK)`, and `ScoreThreshold`
- [x] 2.6 Map Qdrant `ScoredPoint` payload back to `ChunkMetadata`, construct `RetrievalResult` list
- [x] 2.7 Register `IRagRetrievalService → RagRetrievalService` as scoped in `Program.cs`

## 3. Public Retrieval Endpoint

- [x] 3.1 Create `Features/Retrieval/RetrievalEndpoints.cs` with minimal API endpoint definitions
- [x] 3.2 Implement `GET /retrieval/search` — bind query params to `RetrievalQuery`, call `IRagRetrievalService.SearchAsync`, return JSON array
- [x] 3.3 Return HTTP 400 if `q` parameter is missing or empty
- [x] 3.4 Map endpoint in `Program.cs`

## 4. Admin Diagnostic Endpoint

- [x] 4.1 Implement `GET /admin/retrieval/search` in `RetrievalEndpoints.cs` — same as public endpoint but returns `RetrievalDiagnosticResult` (includes `PointId`)
- [x] 4.2 Extract point ID from Qdrant `ScoredPoint.Id.Uuid`
- [x] 4.3 Mapped under `/admin` route group (API key middleware already applied)

## 5. Payload → ChunkMetadata Mapping

- [x] 5.1 Create `Features/Retrieval/QdrantPayloadMapper.cs` with static methods mapping Qdrant payload to `ChunkMetadata`
- [x] 5.2 Handle missing payload fields gracefully (defaults/null, no throws)

## 6. Verification

- [x] 6.1 `dotnet build` passes with zero errors
- [ ] 6.2 With a populated Qdrant collection, call `GET /retrieval/search?q=fireball+spell&category=spell` and verify ranked spell results
- [ ] 6.3 Call with `version=Edition2024` filter and confirm all results have correct version metadata
- [ ] 6.4 Call with `version=Edition2014` filter and confirm different results (edition separation works)
- [ ] 6.5 Call `GET /admin/retrieval/search?q=fireball` with API key and confirm `pointId` field is present
- [ ] 6.6 Call `GET /retrieval/search` without `q` parameter and confirm HTTP 400
