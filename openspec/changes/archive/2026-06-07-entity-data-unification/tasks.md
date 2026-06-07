## 1. Entity ID Unification (EntityIdSlug)

- [x] 1.1 Add source key aliases to `EntityIdSlug.BookOverrides` — `"TCE"` → `"tce"`, `"PHB"` → `"phb14"`, `"DMG"` → `"dmg14"`, `"XPHB"` → `"phb24"`, `"XDMG"` → `"dmg24"`, `"MM"` → `"mm14"`, `"MM25"` → `"mm24"`, `"XGTE"` → `"xgte"`, `"MPMM"` → `"mpmm"`, `"VGM"` → `"vgm"`, `"ERLW"` → `"erlw"`
- [x] 1.2 Update display name overrides that used non-source-key slugs — `"Tasha's Cauldron of Everything"` → `"tce"`, `"Dungeon Master's Guide"` → `"dmg14"`, `"Xanathar's Guide to Everything"` → `"xgte"`, `"Volo's Guide to Monsters"` → `"vgm"`, `"Mordenkainen Presents: Monsters of the Multiverse"` → `"mpmm"`, `"Eberron: Rising from the Last War"` → `"erlw"`
- [x] 1.3 Add unit tests verifying source key and display name produce identical slugs for TCE, PHB, DMG

## 2. Deterministic Qdrant Point UUIDs

- [x] 2.1 Replace `Guid.NewGuid()` in `QdrantEntityVectorStore.ToPoint` with `Guid.CreateVersion5(s_entityNs, Encoding.UTF8.GetBytes(p.Envelope.Id))` using DNS namespace GUID as `s_entityNs`
- [x] 2.2 Add `s_entityNs` as a `private static readonly Guid` field on `QdrantEntityVectorStore`
- [x] 2.3 Add unit test verifying same entity ID produces same UUID across two calls

## 3. GetByIdsAsync on IEntityVectorStore

- [x] 3.1 Add `Task<IReadOnlyDictionary<string, EntityEnvelope>> GetByIdsAsync(IList<string> entityIds, CancellationToken ct = default)` to `IEntityVectorStore`
- [x] 3.2 Implement in `QdrantEntityVectorStore` — build a `Should` filter over entity ID payload field, paginate with `ScrollAsync` to handle batches >1000, return dict keyed by entity ID
- [x] 3.3 Add unit/integration test covering known IDs returned, unknown IDs absent, large batch

## 4. EntityMerger

- [x] 4.1 Create `Features/Ingestion/Entities/EntityMerger.cs` with `public static EntityEnvelope Merge(EntityEnvelope canonical, EntityEnvelope existing)` applying the field priority table from design.md
- [x] 4.2 Implement keyword merge — take whichever list is longer
- [x] 4.3 Implement page merge — existing wins if set, canonical wins otherwise
- [x] 4.4 Implement type merge — existing type wins if canonical type is `Class` (default), otherwise canonical type wins
- [x] 4.5 Add unit tests for each merge rule (canonical fields win, 5etools srd wins, keyword length rule, page fallback, type fallback)

## 5. Wire Merge into EntityIngestionOrchestrator

- [x] 5.1 After ID rewriting in `IngestEntitiesAsync`, batch-fetch existing envelopes via `store.GetByIdsAsync(entityIds)`
- [x] 5.2 For each entity to render, if an existing envelope is found, call `EntityMerger.Merge(canonical, existing)` before rendering canonical text
- [x] 5.3 Remove the `DeleteByFileHashAsync($"5etools:{key}")` bulk delete — deterministic UUIDs handle deduplication via upsert
- [x] 5.4 Keep `DeleteByFileHashAsync(record.FileHash)` to clean up previous canonical ingest runs
- [x] 5.5 Add integration test: 5etools entity upserted first, canonical ingest runs, result has canonical fields + 5etools srd flags

## 6. Canonical Type Fixer Endpoint

- [x] 6.1 Create `Features/Admin/CanonicalTypeFixerService.cs` — loads canonical JSON, loads 5etools data in memory, matches by name+sourceBook, applies correct type, rewrites entity IDs and internal cross-reference strings, saves file
- [x] 6.2 Register `POST /admin/canonical/fix-types` in admin endpoints, accepting `book` query param, returning 200 with fix summary or 404 if book not found
- [x] 6.3 Implement internal cross-reference rewriting — after all ID renames are computed, do a string replace pass on the full serialized JSON replacing old IDs with new IDs
- [x] 6.4 Add unit test: entity typed as Class gets correct type from 5etools lookup, ID is rewritten, cross-refs updated
- [x] 6.5 Add entry to `DndMcpAICsharpFun.http` and `dnd-mcp-api.insomnia.json` for the new endpoint

## 7. Extraction Prompt Update

- [x] 7.1 Locate the entity extraction system prompt file
- [x] 7.2 Add the full list of valid `EntityType` values with classification guidance and examples
- [x] 7.3 Add the rule: prefer most specific applicable type; use `Class` only as last resort

## 8. Migration

- [x] 8.1 Run `POST /admin/5etools/import` to re-import all 5etools data with deterministic UUIDs
- [x] 8.2 For each canonical book (phb14, tce, dungeon-master-s-guide): run fix-types, validate, re-ingest
- [x] 8.3 Verify search results show no duplicate entity IDs
