## Why

After two AI review passes over the review pile, 83 entities are confirmed junk: an art credit, a map label, a
scrap of column heads, or text another entity of the same book already holds in full. There is no way to close
them, so they stay `NeedsReview` forever and bury the 40 entities that do need a person. Lead-in-only Lore is
already declined automatically; junk needs the same outcome by an explicit, recorded decision.

## What Changes

- `<book-slug>.overrides.json` gains a `declines` list: `{entityId, reason, at}`.
- Applying overrides (every extraction run and `apply-overrides`) gives a declined entity disposition `Declined`
  and `needsReview: false`, whatever its checks or approval. Declined entities are already kept out of
  `dnd_entities` and off the review page's flagged list; the entity stays in the canonical file for audit.
- Declines survive re-runs, follow the override id-migration like approvals, and an orphaned decline is listed with
  the other orphans.
- Review-page edits (save/approve/accept-new) keep the file's declines.

## Capabilities

### New Capabilities

### Modified Capabilities

- `entity-verification-review`: overrides file holds declines; a declined entity is `Declined`.

## Impact

- `Features/Ingestion/Programmatic/EntityOverrides.cs`, `OverridesStore.cs`, `OverrideMerger.cs`,
  `OverrideMigration.cs`, `Features/Admin/Review/EntityReviewService.cs`, tests.
- `CompanionUI/Pages/Review/Review.razor`: a Decline button (Blazor calls the service directly, so no HTTP route change).
- Data: the 83 junk verdicts from the AI review are written as declines and applied.
