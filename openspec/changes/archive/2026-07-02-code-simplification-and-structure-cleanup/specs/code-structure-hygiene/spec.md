## ADDED Requirements

### Requirement: Shared logic has a single source of truth

Logic currently duplicated across slices SHALL be consolidated into one shared definition and
referenced everywhere: book-slug derivation, the EntityType→mapper registry, the `Edition2024Sources`
set, renderer helpers (`StripTags`/size/align maps), feature-entry extraction, the sidecar-file
writer, the spell-heading promotion block, the canonical-sidecar `IsSidecar` check, the 5etools
enrich+merge block, and the bounded fuzzy-match scan. Behaviour SHALL be unchanged. (SIM-02, SIM-05,
SIM-06, SIM-07, SIM-08, SIM-12, SIM-13, SIM-14, SIM-15, SIM-16, STR-08, STR-10, STR-11)

#### Scenario: No duplicated mapper registry
- **WHEN** the code is inspected for the EntityType→mapper map and `Edition2024Sources`
- **THEN** each exists in exactly one place, referenced by all consumers

#### Scenario: Behaviour is preserved
- **WHEN** the full test suite runs after consolidation
- **THEN** it passes unchanged (no behavioural difference)

### Requirement: No dead code

Unreferenced members and abstractions SHALL be removed: `CanonicalJson.WriteAsync` and `ReadOptions`,
the `EntityIngestionResult.Enriched` alias, the unused `IEntityCanonicalTextRenderer<TFields>`
abstraction, and stray blank-line/comment residue. (SIM-01, SIM-03, SIM-04, SIM-09, SIM-10, SIM-11)

#### Scenario: Dead members are gone
- **WHEN** `find_referencing_symbols` is run on the removed members
- **THEN** they no longer exist and nothing referenced them

### Requirement: The extraction orchestrator is decomposed

`EntityExtractionOrchestrator` SHALL be split so that candidate scanning/dedup, the shared
per-candidate extraction loop (full and errors-only as a strategy/flag), and top-level orchestration
are separate units, each independently constructible/testable. Behaviour SHALL be unchanged. (STR-09)

#### Scenario: Extraction still produces identical output
- **WHEN** a book is extracted before and after the split
- **THEN** the canonical/errors/warnings output is identical

### Requirement: Layering conventions are consistent

Domain types SHALL NOT carry EF mapping attributes (mapping moves to Fluent config); feature
interfaces SHALL NOT import Infrastructure namespaces they do not use; data-access types SHALL use
`IDbContextFactory` consistently (or document the exception); Admin endpoint files SHALL share one
endpoint-mapping convention with parsing/options extracted; and `Program.cs` SHALL delegate option
binding and reranker DI to per-feature extension methods. (STR-01, STR-02, STR-03, STR-04, STR-05,
STR-06, STR-12, STR-13, STR-16)

#### Scenario: Domain stays persistence-agnostic
- **WHEN** Domain types are inspected
- **THEN** they carry no EF/DataAnnotations mapping attributes (config lives in `AppDbContext`)

#### Scenario: Composition root delegates
- **WHEN** `Program.cs` is read
- **THEN** feature option-binding and reranker registration happen in each feature's `Add*` extension,
  not inline in the composition root

#### Scenario: Admin endpoints share one convention
- **WHEN** the Admin endpoint files are compared
- **THEN** they all map via the `RouteGroupBuilder("/admin")` pattern, with multipart parsing extracted
  from the handler
