## Context

`Campaign` is a plain record — `Campaign(long Id, long UserId, string Name, string Description, DateTime CreatedAt, string? Setting = null)` — with an EF config that only declares a `UserId` index (no navigation property, so the FK-shaped column needs the index declared explicitly). `Setting` was added the same way this change adds its fields: `AddCampaignSetting` is a two-line additive migration, and `SettingCatalog.Resolve(setting)` maps a nullable string to a set of book keys, returning an empty (unscoped) set when null.

That is the precedent to follow, and its shape is already proven end-to-end by `ask_setting_lore`.

Consumers today hold hardcoded scopes: `RuleSources.Keys = ["PHB", "DMG"]`, `DowntimeSources`, `SettingCatalog.SettingBooks`. `RulesAdjudicationService.AskAsync` already accepts a `DndVersion? edition` and threads it into `RetrievalQuery.Version` — but that edition arrives as an **LLM tool argument**, not from the campaign.

## Goals / Non-Goals

**Goals:**

- Let a campaign declare its rules canon and its in-play books, as real tables do.
- One resolver, so no consumer re-derives scope.
- Preserve existing campaigns' behaviour exactly.
- Keep the door open for the 2024 books without blocking on acquiring them.

**Non-Goals:**

- Changing any consumer. `RuleSources` and friends keep their current hardcoded scopes until their own slices. This slice ships the model and the resolver, nothing that alters an answer.
- Removing the LLM `edition` argument — that belongs with the rules-scoping slice that replaces it.
- Per-book rules precedence ordering. Edition is the single tiebreaker; an ordered priority stack was considered and rejected as a debugging burden ("why did it answer 2014?").
- Any UI beyond an edition selector and a book multi-select.

## Decisions

**Edition and allowlist are separate fields, not one derived from the other.** They answer different questions — *which grappling rule* versus *is Ravnica in play* — and collapsing them breaks the common case. Deriving edition from the allowlist fails as soon as both `PHB` and `XPHB` are allowed; deriving the allowlist from edition fails for every 2024 table using a 2014-era setting, which is most of them, since thirteen of sixteen books will never be reissued.

**The allowlist stores `BookCatalog` keys.** Display names are the fragile identifier this codebase has already been burned by — `dnd_blocks.source_book` held display names, `SettingCatalog` matched on them, and the mismatch silently returned zero. Keys are stable, and an unknown key is checkable against the catalog, so the same guard that surfaced TCE's 2,445 orphaned blocks covers this too. The write path validates against `BookCatalog.Keys` and rejects rather than storing an unmatchable entry.

**Persisted as a JSON column.** `HeroSnapshot.CharacterSheet` already establishes JSON-column persistence in this context, and the allowlist is a small value-set owned entirely by its campaign — never queried across campaigns, never joined. A join table would add a migration, a cascade obligation (this repo's `CampaignRepository.DeleteAsync` cascades children by hand, and a new child table that is not added there orphans rows), and buy nothing.

**Mechanics scope versus lore scope is the resolver's core distinction.** The naive model — scope everything by edition — makes a 2024 campaign lose 2,943 entities and every setting book. The correct boundary is not per-book but per-content-type: a setting's history is edition-portable, a subclass is not. The resolver therefore exposes both sets and lets each consumer pick, rather than encoding one policy that would be wrong for half its callers.

**Existing campaigns are seeded explicitly, new ones default to core-only.** These are different populations with different correct answers. Core-only is right for a fresh table declaring its sources; applied retroactively it silently removes thirteen books from campaigns that have been using them. The migration writes the full key list rather than relying on a null-means-everything sentinel, so the state is inspectable and a later book does not silently join every historical campaign.

## Risks / Trade-offs

**The migration is the highest-risk part**, and it is data-touching rather than schema-touching. It must seed from the book keys registered *at migration time*, not from a hardcoded list that will rot. The migration test asserts an existing campaign ends with every current key and `Edition2014` — i.e. that behaviour is preserved, which is the actual contract, not that a specific count appears.

**A stale allowlist hides new content.** Once campaigns hold explicit lists, a newly ingested book joins no existing campaign until added. That is the deliberate cost of explicitness over a null-means-everything sentinel, and it is why the UI multi-select matters — but it is a real papercut worth stating rather than discovering.

**This slice changes no answers, so it cannot be validated by asking a question.** Its gates are unit tests, a real-Postgres migration test, and the empty-migration check that the schema change is exactly what is intended. Resisting the urge to wire a consumer "while we're here" is what keeps that verifiable — and consumers genuinely cannot be validated until the 2024 books exist, since a 2024 campaign would correctly resolve to an empty corpus.

**Adding a table or column to `Campaign` carries the cascade obligation** noted above. A JSON column sidesteps it; if this is ever refactored to a join table, `CampaignRepository.DeleteAsync` must gain a matching `ExecuteDeleteAsync` inside its execution-strategy transaction.
