## 1. BookSourceRegistry

- [ ] 1.1 Create `Features/Ingestion/FivetoolsIngestion/BookSourceRegistry.cs` with `FivetoolsBookInfo` record and singleton service that loads `5etools/books.json` at startup
- [ ] 1.2 Implement `TryGetBook(string sourceKey)` returning `FivetoolsBookInfo?`
- [ ] 1.3 Implement `GetByGroup(string group)` returning `IReadOnlyList<string>` of source keys
- [ ] 1.4 Implement `ResolveIntent(string intent)` with aliases: core/supplement/setting/2014/5e/2024/5.5e/srd/free rules
- [ ] 1.5 Implement `DisplayAbbr` derivation: strip leading X + append `'YY` from published year
- [ ] 1.6 Log warning on startup for any `IngestionRecord` whose `FivetoolsSourceKey` is not in the registry
- [ ] 1.7 Register `BookSourceRegistry` as singleton in `Program.cs`

## 2. Database: FivetoolsSourceKey Column

- [ ] 2.1 Add nullable `FivetoolsSourceKey` string property (max 20) to `IngestionRecord`
- [ ] 2.2 Add EF Core migration `AddFivetoolsSourceKeyToIngestionRecord`
- [ ] 2.3 Verify existing records default to `null` after migration

## 3. Registration Endpoint

- [ ] 3.1 Parse optional `fivetoolsSourceKey` from multipart form in `BooksAdminEndpoints.RegisterBook`
- [ ] 3.2 Validate supplied key against `BookSourceRegistry`; return HTTP 422 if unknown
- [ ] 3.3 Store validated key on `IngestionRecord.FivetoolsSourceKey`
- [ ] 3.4 When key is absent, compute top-3 fuzzy name suggestions and include `suggestedSources` in response

## 4. GET /admin/5etools/sources Endpoint

- [ ] 4.1 Add `GET /admin/5etools/sources` endpoint in `FivetoolsAdminEndpoints` returning all registry entries
- [ ] 4.2 Support optional `?group=` query filter
- [ ] 4.3 Add endpoint to `DndMcpAICsharpFun.http` and `dnd-mcp-api.insomnia.json`

## 5. SRD Flags on EntityEnvelope and Canonical JSON

- [ ] 5.1 Add `Srd`, `Srd52`, `BasicRules2024` bool properties to `EntityEnvelope`
- [ ] 5.2 Update `FivetoolsMapperBase.Map` to read `srd`, `srd52`, `basicRules2024` from the JSON element
- [ ] 5.3 Update canonical JSON entity schema (add `srd`, `srd52`, `basicRules2024` optional bool fields)
- [ ] 5.4 Update `CanonicalJsonLoader` to read and pass through the three SRD flag fields

## 6. Qdrant Payload: SRD Flags + Normalised sourceBook

- [ ] 6.1 Add `Srd`, `Srd52`, `BasicRules2024` constants to `EntityPayloadFields`
- [ ] 6.2 Add keyword index for `srd`, `srd52`, `basicRules2024` in `QdrantCollectionInitializer` for `dnd_entities`
- [ ] 6.3 Write SRD flags to Qdrant payload in the entity ingestion service
- [ ] 6.4 When the book has a non-null `FivetoolsSourceKey`, set `sourceBook` payload field to the key during entity ingestion

## 7. Extraction: Source Key and Edition Propagation

- [ ] 7.1 Pass `FivetoolsSourceKey` (from `IngestionRecord`) into the extraction pipeline context
- [ ] 7.2 Set `source` field in each extracted entity to the source key (when non-null)
- [ ] 7.3 Derive `edition` from `BookSourceRegistry.PublishedYear` (≥2024 → `Edition2024`, else `Edition2014`) when source key is present

## 8. Entity Search: SRD Filter Parameters

- [ ] 8.1 Add `srd`, `srd52`, `basicRules2024` optional bool query params to `EntityRetrievalEndpoints` (public + admin)
- [ ] 8.2 Pass flags through `EntitySearchQuery` → `QdrantEntityVectorStore` filter builder
- [ ] 8.3 Add keyword filter condition in `BuildMustConditions` for each SRD flag when set to `true`

## 9. HTTP Contracts

- [ ] 9.1 Update `DndMcpAICsharpFun.http` with example requests for `GET /admin/5etools/sources`, updated `POST /admin/books/register` with `fivetoolsSourceKey`, and entity search with `srd52=true`
- [ ] 9.2 Sync all changes to `dnd-mcp-api.insomnia.json`
