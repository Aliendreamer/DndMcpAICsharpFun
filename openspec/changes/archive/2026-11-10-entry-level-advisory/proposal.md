## Why

776 programmatic entities wait on the /review page. 258 of them were checked page by page against the book (ChatGPT answers,
2026-11). When `entry-level` is the only failed check, 41 of 68 were already correct and 13 more needed only typo fixes the
check does not detect; only 14 (about 1 in 5) had the boundary problem it looks for. All 6 entities failing `entry-level`
together with `name-words-known` were correct. These flags fill the pile without finding errors.

## What Changes

- A failed `entry-level` check becomes advisory: it no longer makes an entity `NeedsReview` when it is the only failed check,
  or when its only company is a failed `name-words-known`. The check and its reason stay on the entity.
- Every other failed check still blocks, as before. `entry-level` paired with any other check still blocks.
- Existing books pick the new disposition up through the normal apply-overrides step (no re-extraction). About 101–120
  entities leave the pile and are ingested into `dnd_entities`.
- Accepted cost: about 1 in 5 of those entities keep leaked sidebar, caption or stat-block text without a flag.

## Capabilities

### New Capabilities

### Modified Capabilities

- `entity-verification-review`: "Disposition from checks" gains an advisory exception for `entry-level`.

## Impact

- `Features/Ingestion/Programmatic/OverrideMerger.cs` (`SetDisposition`) and its tests.
- Canonical files of all 16 programmatic books after apply-overrides; `dnd_entities` gains the newly accepted entities.
