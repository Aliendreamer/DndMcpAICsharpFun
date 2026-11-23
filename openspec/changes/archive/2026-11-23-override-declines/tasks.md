## 1. Declines in the overrides file

- [x] 1.1 `Decline` record + `EntityOverrides.Declines` (optional third parameter, non-null accessor); `OverridesStore` loads/saves `declines` (missing → empty); store round-trip test
- [x] 1.2 `OverrideMerger.Apply` sets `Declined`/`needsReview:false` for a declined id regardless of checks/approval; tests (declined with failing checks; declined + approval; not-declined unchanged)
- [x] 1.3 `OverrideMigration` migrates decline ids and lists orphaned declines; test
- [x] 1.4 `EntityReviewService` keeps declines when it rewrites the overrides file; test

## 2. Data

- [x] 2.1 Write the AI-review junk verdicts as declines, apply overrides, commit

## 3. Review-page Decline button

- [x] 3.1 `EntityReviewService.DeclineAsync(entityId, reason, ct)`: reason required; writes/replaces the decline, sets `Declined`, reindexes (point deleted); keeps overrides, approvals and other declines; tests
- [x] 3.2 `/review`: Decline button beside Approve for flagged entities; after declining, the list drops the entity (a declined row is never `flagged`) and the page moves on like after an approval
