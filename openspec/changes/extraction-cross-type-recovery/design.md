## Context

Extraction is content-first with a classifier-as-prior union (`deterministic-type-resolution`, §F of the archived prose-grounded design): a candidate's keyword-derived prior is demoted to a hint; the model is offered a pruned type-union `{frequency-floor: Monster/Spell/Item/Class} ∪ {guess + empirical confusion set} ∪ {none}` and either picks a type or declines (`extraction-disposition`). The gate's purpose is anti-fabrication — it must never invent an entity. Observed side effect: a real thing mis-prior'd as `Monster` can be **declined** rather than **re-typed**, because the model judges "this isn't a monster" and picks `none` instead of the correct type.

Observed (PHB corpus re-extraction, 2026-07-18):
- `phb14.monster.light-armor` → declined; but Light Armor is an **Item/Armor**.
- `phb14.class.spellcasting` → declined "not a discrete game entity"; it is a **Rule** (the app has a `Rule` type + the `ask_rules` tool).

## Goals / Non-Goals

**Goals:**

- A mis-prior'd candidate with clear signals for a different *real* type is admitted under that type, not declined.
- An explicit, consistently-applied policy for whether rules content is a `Rule` entity or prose-only.
- Preserve the anti-fabrication guarantee — re-typing is still grounded by the cascade; a re-typed candidate whose fields don't ground is still rejected.

**Non-Goals:**

- Re-admitting genuine noise (chapter headings, TOC). The fix is *re-type real content*, not *lower the bar*.
- Building a new entity type. `Item` and `Rule` already exist.
- Changing the tables work (separate, shipped).

## Decisions (to refine on implementation — investigation-gated)

**D1 — Investigate the actual union first.** Before widening anything, log/inspect the exact type-union offered for `light-armor`/`spellcasting`-shaped candidates and the model's reason. The frequency floor already includes `Item`, so the gap may be (a) the model preferring `none` over `Item` for a *category/table* candidate (Light Armor is the armor-category intro, not a specific armor like "Leather"), or (b) an over-narrow prune dropping the right type. The fix differs per cause; measure before changing.

**D2 — Cross-type re-typing via the confusion set.** If the union lacks the right type for a signalled candidate, extend the empirical confusion set (grounded in the real §A misclassifications) so, e.g., an armor/weapon/item-signal candidate with a `Monster` prior offers `Item`, and a rules-signal candidate offers `Rule`. Keep `none` always offered (a bad widen degrades to a false-decline, never a fabrication — the §F safety property).

**D3 — Rules policy (the modeling decision).** Two options, pick one and apply consistently:
- **(a) Rules stay prose-only** (current): rules like Spellcasting are answered by `ask_rules` over `dnd_blocks`; the decline of a rules candidate is *correct*, and we only fix the item/entity mis-types (D2). Simpler; no new `Rule` entities.
- **(b) Rules become `Rule` entities**: a rules-section candidate is admitted as a `Rule` entity (name + prose fields + provenance), making rules structurally listable/filterable alongside prose retrieval. More coverage; risks flooding `Rule` with every procedural paragraph unless gated by a rules-section signature.
- Recommendation to validate: **(a) for procedural/multi-paragraph rules** (Spellcasting, multiclassing) — prose + `ask_rules` already serve these well; **plus D2** so category *items* (armor, weapons) are captured as `Item`. Revisit (b) only if a concrete query needs rules as a filterable set.

**D4 — Category vs instance.** "Light Armor" is a category header; the queryable items are the specific armors (Leather, Chain Mail…). Part of D1's investigation: are the *individual* armors extracted as `Item`s (and only the category header declined, which is fine), or are the armors themselves also being lost? The fix targets whichever is actually dropping real instances.

## Risks / Trade-offs

- **Widening re-admits noise** → Mitigation: re-type only on a *positive signal* for the new type (item/armor/weapon markers; rules-section markers), keep `none`, and let the grounding cascade reject ungrounded fields. Measure decline/junk deltas on the corpus (offline) before shipping.
- **Rule-entity flooding** (if D3b) → Mitigation: a rules-section signature gate; default to D3a until a query justifies it.
- **Interaction with the running corpus re-extraction** → none; this is a captured follow-up, implemented after the table run, then a targeted re-extract validates the recovered types.
