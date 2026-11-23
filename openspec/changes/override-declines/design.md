## Context

`OverrideMerger.SetDisposition` already returns `Declined` for lead-in-only Lore, and both
`EntityIngestionOrchestrator` (programmatic: only `Accepted` is indexed) and `EntityReviewService` (Declined is not
flagged) handle `Declined`. `EntityOverrides` is a record of `Overrides` and `Approvals`, constructed in many places
with two arguments.

## Goals / Non-Goals

**Goals:** a recorded, re-run-proof way to decline a programmatic entity.

**Non-Goals:** a review-page Decline button or endpoint; deleting entities; un-declining other than by removing the
record from the file.

## Decisions

- `public sealed record Decline(string EntityId, string Reason, DateTimeOffset At);`
- `EntityOverrides(IReadOnlyList<FieldOverride> Overrides, IReadOnlyList<Approval> Approvals, IReadOnlyList<Decline>? Declines = null)`
  with a non-null accessor (empty when absent) — keeps every two-argument call site compiling. JSON key `declines`;
  an old file without it loads as empty.
- The decline wins over everything: applied after `SetDisposition` in `OverrideMerger.Apply` (and in the review
  service's per-entity recompute) → `Disposition = Declined, NeedsReview = false`. Overrides on a declined entity
  are still applied (harmless, kept for audit).
- `OverrideMigration` maps decline ids like approval ids and counts them toward orphans.
- `EntityReviewService` builds new `EntityOverrides` with `o with { … }` so declines are never dropped.

## Risks / Trade-offs

- A declined entity whose text later changes (better extraction) stays declined silently → acceptable: the decline
  records a human/AI judgment about junk; the orphan list catches vanished ids.
