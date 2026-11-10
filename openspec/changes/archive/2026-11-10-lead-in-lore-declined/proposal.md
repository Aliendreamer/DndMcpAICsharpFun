## Why

The extractor makes a Lore entity of every heading, including headings whose only text is one sentence that introduces
what follows ("This section presents one new subclass option…", "Here are sample adventure hooks…", "The spells are
presented in alphabetical order.") and back-cover blurbs ("…for the world's greatest roleplaying game"). They carry no
content, yet 24 of them sit in the review pile and 21 more are served to the chat.

## What Changes

- A Lore entity whose entries are a single sentence that only introduces other content (or a back-cover blurb) gets
  disposition `Declined`: kept in the canonical file for audit, not ingested, not listed as flagged on /review.
- Applies to new extractions and, through apply-overrides, to existing books (45 entities today).

## Capabilities

### New Capabilities

### Modified Capabilities

- `entity-verification-review`: new requirement "Lead-in-only Lore is declined".

## Impact

- `Features/Ingestion/Programmatic/OverrideMerger.cs` (`SetDisposition`), a new `LeadInLore` matcher, the /review list
  (`EntityReviewService.ListAsync`) and tests. Canonical files of the affected books after apply-overrides.
