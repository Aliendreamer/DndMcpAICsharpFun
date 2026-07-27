## Why

For an official book, a section whose page carries a non-entity TOC category is recovered as a candidate **only** when its heading matches the 5etools *monster* roster. Every other entity type in such a section is silently dropped. Three consecutive setting books show what that costs:

- **EGW** skipped **1096** sections. Its entire character-options chapter (pp. 162–190) classifies as `ContentCategory.Rule`, so Echo Knight, Chronurgy Magic, Graviturgy Magic, Hollow One, the four subraces and the dunamancy spells never became candidates. Result: 82 entities against a ~219-entity roster, with **zero** Race, Subrace, Subclass, Spell or Background.
- **SCC** skipped 789 sections; **GGR** skipped 620.

The Monster-only restriction also actively *mis-types*. Because the recovery searches the global index and `EntityNameIndex` loads `bestiary-*` before `races.json`, EGW's race/subrace pages on pp. 162–182 matched other books' bestiary entries and were emitted as **Monster** candidates — `"Sea Elf"`, `"Aarakocra"`, `"Kenku"`, `"Orc"`, `"Tortle"` all landed in `egw.json` as monsters.

The index already covers Spell, Class, Subclass, Monster, Item, MagicItem, Background, Race, Feat, Condition and God. The recovery path simply declines to use any of it beyond `Monster`.

## What Changes

- **Recovery generalizes beyond `Monster`.** A TOC-skipped section whose heading confidently matches *any* indexed 5etools entity is recovered, carrying **the matched entry's type** as its primary prior instead of a hardcoded `Monster`.
- **Matching prefers the book's own 5etools source, falling back to the global index.** EGW's `"Orc"` resolves to EGW's own Race entry rather than a bestiary Orc, which fixes the mis-typing as a side effect of fixing the recall gap.
- **`EntityNameIndex` gains the 5etools `source`** on each entry, so same-source preference is expressible.
- **`EntityNameIndex` loads the `subrace` array** from `races.json` as `EntityType.Subrace`, currently unloaded — which is why Draconblood, Ravenite, Lotusden and Pallid cannot be matched at all.
- `recoverMonsters` is renamed `recoverFromRoster` at its call sites to stop describing behaviour it no longer has.

Cost is bounded by the roster, not by the skip count: only headings matching a real 5etools entity earn an LLM call. Non-matching adventure prose (`"V9. STOREHOUSE"`, `"RETURN TO URZIN"`) stays skipped exactly as today, so precision cannot regress — recovery only ever ADDS candidates, and the downstream decline gate is unchanged.

Explicitly rejected alternatives: extending `HeadingCategoryClassifier.Guess` with more keywords (whack-a-mole per book, does nothing about mis-typing, and the classifier's per-page granularity cannot split a chapter that mixes races and spells); and ungating long runs of skipped sections (~1096 extra LLM calls for EGW alone, leaning entirely on the decline gate to reject prose).

## Capabilities

### New Capabilities

None. This generalizes existing recovery behaviour.

### Modified Capabilities

- `monster-candidate-recovery`: roster recovery is no longer restricted to `Monster`; it recovers any indexed type and carries the matched entry's type as the prior.
- `entity-name-resolution`: the index carries the 5etools `source` and indexes subraces; matching gains same-source preference with global fallback.

## Impact

- `Features/Ingestion/EntityExtraction/EntityNameIndex.cs` — entry tuple gains `Source`; `subrace` array loaded
- `Features/Ingestion/EntityExtraction/EntityNameMatcher.cs` — source-preferred match
- `Features/Ingestion/EntityExtraction/EntityCandidateScanner.cs` — generalized recovery branch, `recoverMonsters` → `recoverFromRoster`
- `Features/Ingestion/EntityExtraction/EntityCandidateBuilder.cs` — passes the book's source key through
- No endpoint, persistence, or schema change; no migration.

**Sequencing:** depends on `extraction-statline-name-repair` landing first, so a single re-extract of EGW/SCC/GGR validates both changes.

**Breaking-ish:** entity ids for the previously mis-typed sections change type segment (`egw.monster.sea-elf` → `egw.subrace.sea-elf`). The three affected books are being re-extracted from scratch, so no in-place id migration is needed — but the ids are not stable across this change.
