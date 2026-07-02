# 0003 — Ingestion depends on Entities as a shared kernel

- Status: Accepted
- Date: 2026-07-02
- Audit reference: STR-07

## Context

`Features/Ingestion` (and its sub-slices `Ingestion.Entities`,
`Ingestion.EntityExtraction`, `Ingestion.FivetoolsIngestion`) consistently reaches into
`Features/Entities` — in particular the loaders and services that own the canonical
entity model:

- `CanonicalJsonLoader` (`Features/Entities/CanonicalJsonLoader.cs`)
- `EntityCanonicalTextDispatcher` (`Features/Entities/CanonicalText/`)
- `EntityReferenceResolver` (`Features/Entities/EntityReferenceResolver.cs`)

The audit (STR-07) noted this makes `Features/Entities` behave as a de-facto shared
kernel for the ingestion pipeline, which the strict "slices don't reach across each
other" reading would flag.

## Decision

Accept `Features/Entities` as an intentional **shared kernel** for canonical-entity
concerns, and document the relationship here rather than restructuring.

Rationale: the coupling is inherent and unidirectional. `Features/Entities` owns the
canonical entity model — the record types, the canonical-text rendering, and reference
resolution. Ingestion's job is to *produce and validate* canonical entity JSON, so it
must render and validate against that model. The dependency therefore flows one way,
Ingestion → Entities, and Entities has no knowledge of Ingestion.

We deliberately do **not** relocate the shared loaders under `Features/Ingestion`.
Although ingestion is their heaviest consumer, they belong to the entity model
(retrieval and resolution paths also read canonical entities), so moving them under
Ingestion would misrepresent ownership and create the opposite cross-slice reach.

## Consequences

- Reviewers should treat `Features/Ingestion → Features/Entities` references as expected;
  they are not a layering violation.
- The directional rule is the invariant to protect: `Features/Entities` must never depend
  **back** on `Features/Ingestion`. Any change that would introduce such a back-edge
  should be rejected or restructured.
- New canonical-entity model types, renderers, or resolvers belong in `Features/Entities`
  (the kernel), and ingestion code consumes them from there.
- If the entity model is ever extracted into its own project/assembly, this shared-kernel
  boundary is the natural seam to cut along.
