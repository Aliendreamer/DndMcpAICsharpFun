# 0001 — Admin is a cross-cutting orchestration slice

- Status: Accepted
- Date: 2026-07-02
- Audit reference: STR-03

## Context

`Features/Admin` binds directly to several other `Features/*` slices (Ingestion,
Ingestion.Entities, Ingestion.FivetoolsIngestion, Resolution, Retrieval) plus
Infrastructure. Measured against the "vertical slices are peers and should not reach
across each other" convention that the rest of the codebase follows, Admin looks like
an outlier with unusually wide fan-out.

The audit (STR-03) flagged this fan-out. On review it is not accidental leakage: the
Admin endpoints exist specifically to drive the ingestion/extraction/projection
pipelines — register a book, ingest blocks, extract entities, validate canonical JSON,
project structured facts, resolve character features. Orchestrating those pipelines is
the slice's entire purpose, so it necessarily depends on the services that own each
step.

## Decision

Treat `Features/Admin` as an intentional **cross-cutting orchestration slice**, not a
peer vertical slice. Its dependency fan-out across the pipeline features is expected and
allowed. We deliberately do **not** introduce per-dependency façade interfaces: they
would add an indirection layer without removing the essential coupling (Admin would
still need to invoke every underlying step), and would make the orchestration harder to
read, not easier.

The constraint we keep is directional: the pipeline feature slices must not depend
**back** on `Features/Admin`. The coupling stays one-way (Admin → features).

## Consequences

- Reviewers should not treat Admin's breadth of `using`/DI dependencies as a smell; it
  is the documented role of the slice.
- If a genuinely reusable capability emerges inside an Admin service, it should move
  down into the owning feature slice (so other callers can use it), not sideways into
  another Admin service.
- A new pipeline step is expected to add a new Admin dependency; that is normal here.
- If Admin ever grows logic that is not orchestration (e.g. business rules that belong
  to a feature), that logic should be pushed into the feature — see the extraction of
  `BookRegistrationService` (audit STR-04) as the pattern to follow.
