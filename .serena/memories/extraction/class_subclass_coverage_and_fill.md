# Class/Subclass extraction gap + how to fill a missing subclass (2026-07-25, from TCE re-extract)

## The gap (root cause — recurring)
**Neither PDF extraction NOR 5etools-backfill reliably covers Class / Subclass / OptionalFeature.**
- `backfill-entities` providers cover only: Armor, Background, BaseItem, Condition, DiseasePoison, Feat, God, Item, MagicItem, Monster, Spell, Trap, Vehicle, Weapon. `FivetoolsCoverageService.cs` states explicitly: Class/Subclass/Race/Subrace have **no provider** (they DO have an EntityType but are out of backfill scope), and **OptionalFeature has no EntityType at all** (fighting styles have nowhere to land).
- So for a book whose value is subclass content, PDF extraction is the ONLY automated source — and it's imperfect.
- **`extract-entities` (MinerU) grounds MOST subclasses but not all**: on TCE it grounded 17/25, declined 8. It correctly declines the subclass's INLINE features (`fey-wanderer-magic`, `form-of-the-beast`, channel-divinity variants) and fighting styles as "rule text, not a discrete entity" — the LLM decline reason. Those features are NOT lost; they live inside the grounded subclass entity (5etools models them inline). The **automatic-decline-recovery** pass re-admits many declined features/rules as Rule/Lore entities (TCE got 126 Rule + 11 Lore — fighting styles ended up here).
- **ERLW's canonical has ZERO Artificer subclasses** — don't assume "it's in the origin book"; verify (`grep` the canonical). Battle Smith/Armorer were absent from both erlw.json AND the TCE Marker extraction.

## Count drop ≠ regression
MinerU TCE = 256 entities vs the old Marker = 408. NOT a loss — Marker inflated its count by wrongly admitting every feature/heading/prose fragment as a separate "Class" entity (306 Class!). MinerU produces proper types (19 Subclass, 126 Rule, 34 MagicItem, 21 Spell, 20 Monster, 11 Lore). Always **diff WHICH entities**, not raw counts. A `.json.bak` (force-reextract backup) + git preserve the prior version for grafting.

## Recipe: fill a missing subclass by hand-authoring from 5etools
When a subclass is genuinely missing (not in the fresh extract, not in a backup, not backfillable):
1. **Pull from 5etools** `5etools/class/class-<class>.json`: the `subclass` entry (metadata + `additionalSpells` + `subclassFeatures` refs) and the matching `subclassFeature` array entries (the actual feature bodies) filtered by `subclassShortName` + `source`. Get the real `page` from the subclass entry.
2. **Construct a canonical `Subclass` entity** matching a GROUNDED one's exact shape (inspect e.g. `tce.subclass.fey-wanderer`): keys `id` (`<slug>.subclass.<name>`), `type:"Subclass"`, `name`, `sourceBook`, `edition`, `page`, **`firstAppearedIn` = an OBJECT `{book, edition, page}` NOT a string** (this was the one validate failure: `FirstAppearance` won't parse a bare string), `revisedIn: []`, `settingTags`, `canonicalText` (flattened feature text), `fields:{entries:[{type:"entries",name,entries:[...]}], className, additionalSpells}`, `dataSource:"5etools-handauthored"`, `authority:"canon"`, `srd/srd52/basicRules2024:false`, `needsReview:false`, `disposition:"Accepted"`, `keywords`.
3. **Graft** into the book canonical: append, assert **unique ids**, write via `docker cp <host copy> <app>:/books/canonical/<slug>.json` (files are root-owned in the container).
4. **Validate** `POST /admin/canonical/validate` — `failures` MUST be `[]` (the 11 phb14/SRD dangling-ref WARNINGS are pre-existing/benign; a 422 with `failures:[]` is only warnings).
5. **`POST /admin/books/{id}/ingest-entities`** → projects the merged canonical into `dnd_entities`. Poll status→8.

TCE final: all subclasses covered = 17 native + 6 grafted from the Marker `.bak` + 2 hand-authored (Armorer p15, Battle Smith p18). Both books' canonicals committed (a7c9f56) — canonicals are the source of truth, re-ingestable without the ~3.5h re-extract.

## Other ops facts confirmed this run
- Bookmarkless PDFs (EE has no embedded bookmarks): the **heading-derived TOC fallback** (`BlockIngestionOrchestrator`/`EntityCandidateBuilder`) handles them — the "requires bookmarks" book.error was STALE from a prior attempt (trust status + logs, not the error field).
- `backfill-entities` REQUIRES a `?type=` query param (per-type); calling it bare → 500.
- Ingestion queue serializes **one job per book** (2nd job for same book → 409); across books it queues.
