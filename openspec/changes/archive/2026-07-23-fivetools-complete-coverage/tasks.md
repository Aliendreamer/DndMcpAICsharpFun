# Tasks — fivetools-complete-coverage (catalog backfill providers + coverage gate; gap-only, report+warn)

## 1. Catalog backfill providers (wrap existing mappers; mirror the 4 existing providers)
- [ ] 1.1 `FeatBackfillProvider` + `BackgroundBackfillProvider` (feats.json / backgrounds.json; wrap `FivetoolsFeatMapper`/`FivetoolsBackgroundMapper`). TDD each: a roster gap → one `5etools-backfill` Accepted entity; a present name → skipped.
- [ ] 1.2 `ConditionBackfillProvider` + `TrapBackfillProvider` + `DiseasePoisonBackfillProvider` + `VehicleBackfillProvider` (their 5etools files; wrap the existing mappers). TDD as 1.1.
- [ ] 1.3 Item/Weapon/Armor providers (the tricky set, design D2): read `items-base.json` (+ mundane `items.json`) and PARTITION by 5etools type code exactly as `FivetoolsItemMapper`/`WeaponMapper`/`ArmorMapper` classify. TDD: a known base-item subset partitions with no overlap/drop; each provider's gap→entity.
- [ ] 1.4 Register all 9 providers in the `IFivetoolsBackfillProvider[]` DI array (`ServiceCollectionExtensions.AddEntityExtraction`) AND the `EntityBackfillEndpointTests` replica. Endpoint/apply now covers them. Test: unique-id invariant + `CanonicalJsonLoader` load round-trip after a multi-type apply on a seeded canonical.

## 2. Coverage reconciliation gate (report + warn, never block)
- [ ] 2.1 `FivetoolsCoverageService.ComputeAsync(record, ct)`: loop all registered providers, call `EntityBackfillService.ComputeAsync` per provider (NO apply), aggregate `BookCoverage {sourceKey, perType[{type, rosterCount, present, missingCount, missingNames}], unmodeled[], totalPresent, totalRoster, coveragePct}`. `unmodeled[]` from a small commented known-5etools-file list (incl. optionalfeatures) so that gap is visible. Non-official → empty. TDD with fake providers (mirror `EntityBackfillServiceTests`).
- [ ] 2.2 `GET /admin/books/{id}/coverage` (admin-gated) → the `BookCoverage`. Update `DndMcpAICsharpFun.http` + `dnd-mcp-api.insomnia.json`. Endpoint test (mirror `EntityBackfillEndpointTests`).
- [ ] 2.3 Fold a coverage warning summary into `POST /admin/canonical/validate` (stays 200/valid — never 422 on coverage). Test.
- [ ] 2.4 Startup coverage log: one `LogWarning` per official book below 100% (mirror the `ScopeHealthCheck` guard shape). Test the warn-below / silent-at-100 paths.

## 3. Live validation (dev-flow data gate — explicit; do NOT skip)
- [ ] 3.1 Run backfill on PHB; review the canonical diff (feats 1→42, backgrounds→20, base items/gear, conditions...); confirm GAP-ONLY (spot-check an extraction-owned entity byte-unchanged), unique ids + load round-trip, no fabrication. Then `GET /coverage` reads ~100% for provided types and SHOWS the optionalfeatures gap in `unmodeled`. Then `ingest-entities` PHB.

## 4. Gates
- [ ] 4.1 `dotnet build` 0/0; FULL `dotnet test` green; `dotnet format` clean; `.http`+insomnia synced; admin-gate/security review on the new endpoint. Whole-branch review (most-capable model).
