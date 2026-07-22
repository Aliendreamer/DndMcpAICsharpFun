## Why

During the corpus table re-extraction we observed **real content declined instead of re-typed**: a gated-prior candidate whose true type differs from its (mis-derived) prior gets rejected rather than admitted under the correct type. Concrete PHB cases: `monster.light-armor` (Light Armor is an **Item/Armor**, not a monster) was declined "not a monster"; `class.spellcasting` (a **Rule**) was declined "not a discrete game entity". The prose survives in `dnd_blocks` and the decline is audited in `.declined.json`, so nothing is silently lost — but the content never becomes a **structured entity**, so `list_entities` / structured queries can't reach Light Armor as an item. Two distinct fixes fall out, captured here for a later implementation session (the current design was the extraction-honesty gate, which correctly avoids fabrication but is losing legitimately-typed content).

## What Changes

- **(1) Cross-type re-typing.** Ensure a mis-prior'd candidate with strong signals for another type can be *admitted under that type* rather than declined. First step is to VERIFY what type-union the content-first classifier actually offers for these candidates (the frequency floor already includes `Item`, so the gap may be in the model choosing `none` over `Item` for a category/table candidate, or in an over-narrow prune) — then widen the confusion set / adjust the prompt so armor/weapon/item-signal content re-types to `Item`, not `Monster`-or-`none`.
- **(2) Rules policy.** Decide whether rules like Spellcasting (and armor-category intros, class-feature procedures) become first-class **`Rule` entities** (the app already has a `Rule` type + `ask_rules`) or stay **prose-only** (the current design). Make the policy explicit and apply it consistently in the decline gate.

## Capabilities

### New Capabilities

- `extraction-cross-type-recovery`: a mis-typed gated-prior candidate with clear signals for a different real type is admitted under the correct type (e.g. armor → `Item`) instead of declined; and an explicit, consistently-applied policy for whether rules content is captured as `Rule` entities or left to prose.

### Modified Capabilities

<!-- likely touches deterministic-type-resolution / content-first-extraction on implementation; assessed then -->

## Impact

- `Features/Ingestion/EntityExtraction/DeterministicTypeResolver.cs` + the content-first union / classifier-as-prior (confusion set) — the re-typing path.
- The decline gate + the `Rule`-vs-prose policy decision (may touch `IsRealEntity` / the disposition logic).
- Investigation first (what union is offered for the observed cases) before any code.
- **Out of scope now:** implementation — this is a captured follow-up; the corpus table re-extraction is running and this is not part of it.
