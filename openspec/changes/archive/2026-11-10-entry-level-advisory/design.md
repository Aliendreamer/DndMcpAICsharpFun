## Context

`OverrideMerger.SetDisposition` is the single place that turns checks plus an approval into `Accepted` or `NeedsReview`.
It runs on extraction (`Apply`), on apply-overrides (`Reapply`) and on every save from the review page.

## Decisions

- **Rule:** let F be the failed checks the approval does not waive. The entity is blocked unless F is empty, or F contains
  `entry-level` and every member of F is `entry-level` or `name-words-known`.
- `name-words-known` alone still blocks (sample: 3 of 4 had real errors).
- The advisory set is a named constant in `OverrideMerger`, next to `IsWaivable`.
- Drift and unapplied-override checks are never advisory (they are never in the set).
- Approving such an entity is unnecessary; existing approvals are untouched.

## Evidence

258 ChatGPT page checks joined to each entity's failed checks (scratch script; answers in the session scratchpad):
`entry-level` alone 68 → 41 correct, 13 typo-only, 14 boundary errors; `entry-level` + `name-words-known` 6 → 6 correct;
`text-complete` 168 → 6 correct (stays blocking). The sample also chose the rule, so the rate is optimistic.

## Rollout

Rebuild the app container, then `POST /admin/books/{id}/apply-overrides` for each programmatic book; commit the canonical
files per book. Count the /review pile before and after.
