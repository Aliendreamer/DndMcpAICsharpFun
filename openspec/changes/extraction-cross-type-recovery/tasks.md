# Tasks — extraction-cross-type-recovery (CAPTURE-ONLY; implement after the corpus table run)

> Deferred. Captured from the 2026-07-18 PHB re-extraction observation (Light Armor declined
> instead of typed Item; Spellcasting declined as a rule). Do NOT implement while the corpus
> table re-extraction is running.

## 1. Investigate (measure before changing)

- [ ] 1.1 For the observed cases (`monster.light-armor`, `class.spellcasting`, and similar), log/inspect the EXACT type-union the content-first classifier offered and the model's decline reason. Confirm whether `Item`/`Rule` were even on offer (the frequency floor already includes `Item`).
- [ ] 1.2 Determine root cause per case: (a) model preferred `none` over an offered `Item` for a category/table candidate; (b) an over-narrow prune dropped the right type; (c) the candidate is a category header whose individual instances (specific armors) ARE captured — in which case only the header decline is correct.
- [ ] 1.3 Check whether the individual armors (Leather, Chain Mail, …) and weapons are extracted as `Item`s today, or also lost (D4).

## 2. Cross-type re-typing (D2)

- [ ] 2.1 Based on 1.x, extend the empirical confusion set / prompt so a mis-prior'd candidate with positive signals for another real type offers that type (armor/weapon/item signal → `Item`). Keep `none` always offered.
- [ ] 2.2 Regression: measure decline/junk deltas offline against the corpus before shipping (re-type real content without re-admitting noise).

## 3. Rules policy (D3)

- [ ] 3.1 Decide + document the policy: (a) rules stay prose-only (`ask_rules`), or (b) rules become `Rule` entities (gated by a rules-section signature). Recommendation: (a) for procedural rules + (2) for item categories; revisit (b) only if a query needs rules as a filterable set.
- [ ] 3.2 Apply the policy consistently in the decline gate; if (b), add the rules-section signature + admission.

## 4. Verify

- [ ] 4.1 Build clean + full suite green; unit tests for the re-typing (armor-signal Monster-prior → `Item`; noise still declined) and the rules policy.
- [ ] 4.2 Targeted re-extract of a book (e.g. PHB) confirms armor items are now `Item`s (not in `.declined.json`) and rules are handled per policy.
