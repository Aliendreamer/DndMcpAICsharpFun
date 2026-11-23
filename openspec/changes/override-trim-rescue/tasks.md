## 1. Rescue in the merger

- [ ] 1.1 `TrimRescue` helper: removed-line detection, rescue entity construction (id, name, entries, check), unit tests for the spec scenarios
- [ ] 1.2 Wire into `OverrideMerger.Apply`/`Reapply`: compute rescues after the main loop, drop stale rescues on reapply, run rescue entities through the same override/approval/decline handling, rescue ids not orphans; tests (decline persists, no duplicates, disappears when held)

## 2. Data

- [ ] 2.1 Rebuild app, apply-overrides on all 16 books, count rescues and rescued lines, run the committed coverage scan, commit
- [ ] 2.2 AI review loop over the new flagged rescue entities, apply, commit
