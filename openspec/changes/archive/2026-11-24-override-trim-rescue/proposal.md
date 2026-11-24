## Why

Review overrides often trim text that does not belong to an entity — lore glued into a stat block, a neighbouring
section run into a Lore entry. The trim is right, but the trimmed text then belongs to no entity. A scan on
2026-11-24 found about 2,600 printed lines removed this way and held nowhere: 1,383 lines from 167 Monsters, about
1,200 from roughly 730 prose entities. That text is lost from the chat although the book prints it.

## What Changes

- When overrides are applied (fresh extraction and `apply-overrides`), printed lines that a `canonicalText` override
  removes from its entity, and that no other entity of the book holds, are kept in a new flagged Lore entity
  ("trimmed text") instead of being dropped.
- The trimmed-text entity has a stable id derived from its host, so later reviews (fixes, approvals, declines) of it
  survive re-runs; it is rebuilt on every apply and disappears when nothing is left to rescue.
- No change to how the override itself is applied, to drift, or to the review page.

## Capabilities

### New Capabilities

- `override-trim-rescue`: keeping printed text that a review override removes from its entity and nobody else holds.

### Modified Capabilities

None.

## Impact

- `Features/Ingestion/Programmatic/OverrideMerger.cs` (+ a small `TrimRescue` helper), tests in
  `DndMcpAICsharpFun.Tests` (OverrideMerger tests).
- Data: apply-overrides on all 16 books creates the rescue entities (~900, flagged); they go through the AI review loop.
- No endpoint or UI change.
