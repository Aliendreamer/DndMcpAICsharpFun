# 0002 — The canonical directory is configured per pipeline phase

- Status: Accepted (with a known consistency caveat)
- Date: 2026-07-02
- Audit reference: STR-05

## Context

The path to `books/canonical/` (where hand-correctable canonical entity JSON lives) is
exposed through **two** options types, each bound from a different configuration
section:

- `EntityExtractionOptions.CanonicalDirectory` — bound from the `EntityExtraction`
  section. Consumed by the extraction phase and by the admin services
  `CanonicalValidationService`, `CanonicalNameNormalizerService`, and
  `NeedsReviewService`.
- `EntityIngestionOptions.CanonicalDirectory` — bound from the `EntityIngestion`
  section. Consumed by the ingestion phase and by the admin service
  `CanonicalTypeFixerService`.

Both default to `books/canonical`, so in the default configuration they agree. The audit
(STR-05) flagged that two option types describe the same concept, and that admin
canonical-file services are split across them.

## Decision

Keep the two options types for now, one per pipeline phase (extraction vs. ingestion),
rather than consolidating them into a single shared canonical-directory option.

Rationale: `EntityExtraction` and `EntityIngestion` are distinct pipeline phases with
their own option groups (models, directories, batching, and phase-specific flags).
Folding just the directory into a shared type would either split each phase's config
across two sections or force an unrelated coupling between the two option groups. Given
both default to the same path and the coupling is benign in practice, the documentation
cost is lower than the refactor cost.

## Consequences

- **Consistency caveat:** if an operator overrides `EntityExtraction:CanonicalDirectory`
  or `EntityIngestion:CanonicalDirectory` in configuration, they must set **both** to
  the same value. Setting only one makes the extraction/validation services and the
  ingestion/type-fixer services disagree on where canonical files live. This is the one
  real hazard of keeping two keys, and it is the reason this ADR exists.
- Any deployment that relocates `books/canonical/` must update both keys together.
- Future work (deferred): introduce a single shared `CanonicalPaths` option (or a common
  base) that both phase option groups reference, so there is one source of truth for the
  directory while each phase keeps its own remaining settings. This ADR should be
  superseded if that consolidation lands.
