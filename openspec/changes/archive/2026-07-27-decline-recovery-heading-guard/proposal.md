## Why

The automatic decline-recovery pass manufactured **97 non-entities** on Guildmasters' Guide to Ravnica — `Boros Creatures`, `Golgari NPCs`, `Zombies`, `Insects and Arachnids`, `Chapter 5: Treasures`, `Aurelia's`. The correspondence is exact in both directions: every one of GGR's 97 `Lore`/`Rule` entities came from this pass, and every entity the pass produced was `Lore` or `Rule`. That was 34% of the book's entities and drove its "thin" rate (≤2 field keys) to 48% against 22% for EGW and 27% for SCC. It has been cleaned out of the canonical by hand, but nothing stops the next section-organised book from reproducing it.

The existing spec is not wrong — `automatic-decline-recovery` already requires that "a pure chapter heading / TOC entry / fragment" stays declined. It just delegates that judgment entirely to the model: the pass re-offers the candidate a `[Rule, Lore, none]` union and trusts qwen3:8b to answer `none`. On a book whose chapters are organised as guild sections, it answered `Rule`/`Lore` 97 times. Model judgment is the wrong instrument for a decision that is decidable from the name alone.

EGW and SCC triggered the same pass 10 and 12 times, so this is not uniform — it is a book-shape trigger, which is exactly why it will recur silently.

## What Changes

- Add a **deterministic name-shape guard** that runs **before** the recovery LLM call. A candidate whose name is a section or category heading is left declined and never re-offered.
- Rejected shapes, all derived from the observed GGR output:
  - a **category-plural suffix** — name ends in `Creatures`, `Items`, `NPCs`, `Spells`, `Actions`, `Lairs`, `Options`, `Features`, `Proficiencies`
  - a **bare creature-type plural** — `Aberrations`, `Beasts`, `Demons`, `Giants`, `Humanoids`, `Monstrosities`, `Undead`, `Vampires`, `Zombies`, and the rest of the type vocabulary, including two joined by `and` (`Insects and Arachnids`)
  - a **book-structure heading** — `Chapter <n>: …`, `Bestiary`, `Appendix …`
  - a **truncated possessive** — a name that is nothing but `<Name>'s`
- Log each guarded rejection so the pass's suppressions are visible rather than silent.

Not in scope: changing what the recovery does with candidates that pass the guard, the grounding cascade, the disposition policy, or the main extraction gate. This narrows the pass's *input*, nothing else.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `automatic-decline-recovery`: adds a deterministic pre-LLM name guard, so the "true noise stays declined" requirement no longer rests solely on the model choosing `none`.

## Impact

- `Features/Ingestion/EntityExtraction/DeclineRecovery.cs` — guard at the top of `TryRecoverAsync`
- A new name-shape predicate (sibling of the existing `ExtractionSignatures.IsEntityLikeName` gate)
- No endpoint, schema, persistence or migration change.

**Blast radius is deliberately small.** Every candidate reaching this pass has *already been declined* by the main gate. A false positive therefore costs nothing that was otherwise being kept — it leaves a candidate declined that was already declined — whereas a false negative ships a fabricated entity into `dnd_entities`. That asymmetry justifies a guard tuned aggressively, and it is the reason this belongs in the recovery pass rather than in the shared `IsEntityLikeName` gate, where the same patterns would suppress real primary-path entities.

**A saved cost, not just a quality win:** the 97 GGR candidates each consumed a recovery LLM call before producing junk. The guard rejects them for free.
