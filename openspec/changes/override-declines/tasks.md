## 1. Declines in the overrides file

- [ ] 1.1 `Decline` record + `EntityOverrides.Declines` (optional third parameter, non-null accessor); `OverridesStore` loads/saves `declines` (missing → empty); store round-trip test
- [ ] 1.2 `OverrideMerger.Apply` sets `Declined`/`needsReview:false` for a declined id regardless of checks/approval; tests (declined with failing checks; declined + approval; not-declined unchanged)
- [ ] 1.3 `OverrideMigration` migrates decline ids and lists orphaned declines; test
- [ ] 1.4 `EntityReviewService` keeps declines when it rewrites the overrides file; test

## 2. Data

- [ ] 2.1 Write the AI-review junk verdicts as declines, apply overrides, commit
