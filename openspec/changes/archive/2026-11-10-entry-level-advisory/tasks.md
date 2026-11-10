## 1. Disposition rule

- [x] 1.1 Tests in the OverrideMerger test class for the four new scenarios (entry-level alone, with name-words-known,
  name-words-known alone, entry-level + text-complete)
- [x] 1.2 `SetDisposition` treats failed `entry-level` (alone or with `name-words-known`) as advisory; full test suite green

## 2. Rollout

- [x] 2.1 Rebuild the app container; apply-overrides for every programmatic book; pile count before/after
- [x] 2.2 Commit the canonical files per book
