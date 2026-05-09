# Plan Series — structured-entity-extraction

This change ships in three sequential implementation plans. Each plan produces working, testable software on its own. Do **not** archive the change until all three are complete.

## Plans

- [ ] **Plan 1 — Entity foundation + retrieval (no LLM)** → `plan.md`
  - Entity model, per-type field records, slug generator, JSON-schema validators
  - Canonical JSON loader + reference resolver + duplicate-ID detector
  - Per-type canonicalText renderers (Class, Monster first)
  - `dnd_entities` Qdrant collection + payload indexes
  - Entity ingestion path (reads canonical JSON, embeds, upserts)
  - `POST /admin/books/{id}/ingest-entities`
  - `GET /retrieval/entities/{id}`, `GET /retrieval/entities/search`, admin diagnostic
  - Book deletion extension (entity points + canonical JSON cleanup)
  - Ships with hand-written canonical JSON fixtures; no LLM dependency
  - Spec coverage: `structured-entities`, `entity-vector-store`, `rag-retrieval` delta, partial `ingestion-pipeline` delta

- [ ] **Plan 2 — LLM extraction pipeline** → `plan-2.md` (to be written after Plan 1 ships)
  - `IEntityExtractionLlmClient` abstraction + chosen-LLM implementation
  - Schema-constrained extraction with bounded retries and errors-file output
  - Docling output reuse (no double layout pass)
  - Atomic-write canonical JSON output with reference-resolution post-pass
  - `POST /admin/books/{id}/extract-entities` (with `?force=true`)
  - Progress and summary logging at the cadence required by the spec
  - Spec coverage: `entity-extraction-pipeline`, remainder of `ingestion-pipeline` delta

- [ ] **Plan 3 — Backfill and rollout** → `plan-3.md` (to be written after Plan 2 ships)
  - End-to-end validation against the five canonical example queries
  - Backfill canonical JSON for existing registered books one at a time
  - Hand-correction workflow documented in README and CLAUDE.md
  - Operator workflow: register → block-ingest → entity-extract → review JSON in PR → entity-ingest

## Rationale for splitting

- Plan 1 is mostly mechanical .NET + Qdrant work that produces a complete vertical slice (data model end-to-end). Writing a fully detailed TDD plan for it is reasonable and the steps will not bit-rot.
- Plan 2 has an undecided technical question (which LLM, JSON-schema format) that benefits from being resolved against the consumer code from Plan 1 rather than being guessed up-front.
- Plan 3 is a workflow plan that depends on what we learned from Plans 1 and 2.

When Plan 1 ships and is reviewed, invoke `superpowers:writing-plans` again pointing at this change to write Plan 2.
