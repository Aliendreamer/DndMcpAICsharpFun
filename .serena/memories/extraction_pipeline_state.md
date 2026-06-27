# Entity-extraction pipeline — current state (2026-06-26)

**Content-first honesty pipeline is SHIPPED and validated across 3 books (MM, PHB, DMG).** Does NOT overfit to the Monster Manual: spells→Spell, classes→Class, races→Race, items→Item/MagicItem, planes→Plane, traps→Trap; non-entity prose/rules are correctly DECLINED (disposition enum Accepted/NeedsReview/Declined/Failed), never fabricated.

## Key components (Features/Ingestion/EntityExtraction/)
- `ExtractionSignatures` — ONE static utility: `IsCompleteStatBlock` (AC+HP+Challenge = Monster signature), `IsMagicItem` (attunement/wondrous/rarity-header), `IsEntityLikeName` (rejects headings/fragments), primitives `HasArmorClass/HasHitPoints/HasChallenge`. Replaced 4 scattered matchers + deleted `StatBlockSignature`.
- `DeterministicTypeResolver.Resolve(candidate)` → `Drop | Force(type) | Defer`. Ladder: non-entity-name→Drop, complete stat block→Force(Monster), magic-item→Force(MagicItem), else→Defer to content-first union.
- `EntityExtractionOrchestrator.ExtractOneAsync` routes through the resolver; drops non-entity-named candidates before the loop; forced types extract with that type's schema (no decline); else union pick-or-decline.
- `StatBlockScanner` recovers headerless monster stat blocks. `CandidateExtractor.ExtractUnionAsync` = discriminated-union pick-or-decline under grammar-constrained decoding; `MaxTypeDecisionChars=8000` caps type-decision input.

## Status
- main: slug-from-FivetoolsSourceKey fix + text-cap (commit a965aab).
- Branch `consolidate-extraction-signatures`: consolidation + 3 behavior fixes (override misfire guard, drop non-entity-named, magic-item→MagicItem). 699 tests green, opus-reviewed (corpus-validated: 0 real entities dropped, 51 mis-typed Items upgraded). READY TO MERGE pending DMG validation re-run.

## Conventions
- Book slug + entity ids derive from `FivetoolsSourceKey` (PHB→phb14) via `EntityIdSlug` override table — always pass the key for official WotC books.
- ALL code edits via Serena symbolic tools; built-in Read/Edit on .cs forbidden. Grep-verify after Serena edits (insert_after_symbol has silently no-op'd).
- Tests: real Postgres via Testcontainers; non-persistence tests need no DB.

## Next / roadmap
- Clean stale `books/canonical/playerhandbook-2014.json` orphan (a PHB resolver re-run writes correct `phb14.json`, then delete it).
- Real goal = prose-grounded knowledge model + deterministic character-resolution ENGINE (query Bins A/B/C/D). Build-chain what-if = Bin D-plan (engine as pure fn over hypothetical state), NOT a graph DB — Neo4j deferred (D&D = tables + 1-hop refs → Postgres JOINs). Parked spec: openspec/changes/prose-grounded-knowledge-model/.