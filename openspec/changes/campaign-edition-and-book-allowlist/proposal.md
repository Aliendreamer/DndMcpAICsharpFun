## Why

D&D now has **two canons**, and the corpus is about to hold both. Today it is 16 books / ~5,800 entities, all Edition2014. The revised core — `XPHB` (Sept 2024), `XDMG` (Nov 2024), `XMM` (Feb 2025) — is registered in `BookCatalog` but not yet acquired.

Nothing in the app can express which canon a table plays. `RuleSources.Keys` is hardcoded `["PHB", "DMG"]`, so the moment `XPHB`/`XDMG` are ingested, `ask_rules` will blend both rulesets in one answer — and for the rules that actually changed (grappling, exhaustion, backgrounds, weapon mastery) that means confidently mixing incompatible mechanics. Worse, the edition is currently supplied by the **LLM as a tool argument** (`"edition is optional (\"2014\"/\"2024\"); omit to search all editions"`), which delegates to qwen3:8b a question the campaign already answers — the same mistake the decline-recovery guard was built to stop.

A pure edition switch is not enough either. Thirteen of the sixteen books are supplements and settings — Xanathar's, Tasha's, Fizban's, Bigby's, and all five setting books (Eberron, Ravenloft, Ravnica, Wildemount, Strixhaven) — **2,943 entities that will never get a 2024 equivalent**. Scoping a 2024 campaign strictly by edition would answer "not in your edition" to *"tell me about the Dimir guild"*, forever.

Real tables do not declare an edition; they declare **sources**: "we run 2024 rules, we're playing in Eberron, and I allow Xanathar's."

## What Changes

- `Campaign` gains **`Edition`** (`DndVersion`, non-null, default `Edition2014`) — the **rules canon**. It resolves mechanics conflicts when both canons' books are in scope.
- `Campaign` gains a **source-book allowlist** — a set of `BookCatalog` keys controlling **availability**. Stored as keys, never free text, so it inherits the catalog-drift guard and an unknown key fails loud instead of silently matching nothing.
- The two are deliberately separate concerns: edition answers *"which grappling rule?"*, the allowlist answers *"is Ravnica in play?"*.
- **New** campaigns default to **core-only for their edition** (2014 → `PHB/DMG/MM`; 2024 → `XPHB/XDMG/XMM`); supplements are opt-in.
- **Existing** campaigns are migrated to the full current 16-key list with `Edition2014`, preserving today's behaviour exactly.
- A single resolver turns a campaign into its effective scope, so no consumer re-derives it.
- UI: an edition selector and a book multi-select on the campaign form.

Out of scope for this slice — each is its own spec cycle, and none can be validated end-to-end until the 2024 books are ingested: rules/lore/downtime scoping (`RuleSources` et al. intersecting the allowlist, and removing the LLM `edition` argument), entity-lookup scoping, and character-build scoping.

## Capabilities

### New Capabilities

- `campaign-edition-scope`: a campaign declares its rules canon and its in-play source books, and a resolver exposes the effective scope for retrieval.

### Modified Capabilities

None. Consumers are unchanged in this slice — they keep their current hardcoded scopes until their own slices land.

## Impact

- `Domain/Campaign.cs` — two new properties
- `Infrastructure/Persistence/AppDbContext.cs` — allowlist stored as a JSON column (the `HeroSnapshot.CharacterSheet` precedent)
- `Migrations/` — one additive migration, mirroring `AddCampaignSetting`
- `Features/Campaigns/` — repository, resolver, create/update paths
- `CompanionUI/Components/` — edition selector + book multi-select
- No endpoint contract change beyond the campaign create/update payload (`.http` + insomnia updated in the same commit).

**Migration is the risk.** Defaulting *existing* campaigns to core-only would silently remove 13 books from live campaigns — a regression dressed as a default. The migration therefore seeds the full current list explicitly; core-only is the right default for new tables, not a retroactive one.
