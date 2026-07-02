## Why

The largest group of audit findings is non-behavioural cleanup: duplicated logic (book-slug
derivation repeated ~14×, the EntityType→mapper registry and `Edition2024Sources` set defined twice,
renderer helpers and canonical/sidecar writers copy-pasted), dead code (`CanonicalJson.WriteAsync`,
`IReranker`-adjacent unused members, unreferenced options/fields), a 644-line god-file orchestrator,
Domain types carrying EF attributes, and `Program.cs` composition-root sprawl. None change behaviour,
but together they tax every future change to ingestion and the admin slice. Consolidating them in one
cleanup change keeps the diff reviewable and the risk low.

Closes audit findings: **STR-01, STR-02, STR-03, STR-04, STR-05, STR-06, STR-07, STR-08, STR-09,
STR-10, STR-11, STR-12, STR-13, STR-16, SIM-01, SIM-02, SIM-03, SIM-04, SIM-05, SIM-06, SIM-07,
SIM-08, SIM-09, SIM-10, SIM-11, SIM-12, SIM-13, SIM-14, SIM-15, SIM-16**.

## What Changes

- **De-duplicate shared logic:** one book-slug helper (SIM-07), one `FivetoolsMapperRegistry`
  (SIM-14, STR-10), one `Edition2024Sources` source (SIM-15, STR-11), shared renderer helpers and
  feature-entry extraction (SIM-05, SIM-06), one sidecar-file writer (SIM-13), one heading-promotion
  local function (SIM-16), one canonical-sidecar `IsSidecar` (SIM-02), one enrich+merge helper
  (SIM-08, STR-08), one fuzzy-match scan (SIM-12).
- **Remove dead code:** `CanonicalJson.WriteAsync` + `ReadOptions` (SIM-10, SIM-11), unused
  `EntityIngestionResult.Enriched` alias (SIM-09), unused `IEntityCanonicalTextRenderer<TFields>`
  abstraction (SIM-04), stray blank-line/comment gaps (SIM-01, SIM-03).
- **Split the god file (STR-09):** decompose `EntityExtractionOrchestrator` into a candidate
  pipeline, an extraction runner (full/errors-only strategy), and a thin orchestrator.
- **Restore layering conventions:** move EF mapping attributes off Domain types to Fluent config
  (STR-01, STR-02), drop the unnecessary Infrastructure `using` in a feature interface (STR-12),
  adopt `IDbContextFactory` (or document the exception) in `IngestionTracker` (STR-13), standardize
  Admin endpoint-mapping convention and extract the multipart parsing / shared options (STR-03,
  STR-04, STR-05, STR-06), and move inline option-binding and reranker DI out of `Program.cs` into
  per-feature extensions (STR-16).

## Capabilities

### New Capabilities

- `code-structure-hygiene`: the internal-quality contract — single-source-of-truth for shared logic,
  no dead code, layered dependency conventions, and a composition root that delegates to per-feature
  DI extensions. Behaviour is unchanged; this capability constrains structure.

### Modified Capabilities

<!-- None; behaviour-preserving refactor. -->

## Impact

- Broad but behaviour-preserving edits across `Features/Ingestion/**`, `Features/Entities/**`,
  `Features/Admin/**`, `Domain/**`, and `Program.cs`. No data-model or API-contract changes.
- Regression safety rests on the existing test suite staying green; no new behaviour is introduced.

## Non-goals

- Any behaviour change, bug fix, or performance change (those live in the correctness/retrieval/
  security changes).
- Introducing new abstractions beyond consolidating existing duplicates.
