# Tasks — extraction-content-classification (CAPTURE-ONLY; foundational, phased, later)

## 1. Design the taxonomy
- [ ] 1.1 Enumerate the non-entity categories worth structuring (Rule, Lore, Table, Variant/Sidebar) and reconcile with the existing block `ContentCategory` + `ask_rules`/`ask_setting_lore` so entity + retrieval share ONE taxonomy.
- [ ] 1.2 Decide the record shape for a non-entity classification (category + name + prose + provenance; Table links to CanonicalTable).

## 2. Extend the classifier
- [ ] 2.1 Extend the content-first discriminated union from `{entity types} ∪ none` to `{entity types} ∪ {non-entity categories} ∪ {true noise}`; keep the anti-fabrication guarantee (a category classification is still grounded).
- [ ] 2.2 Shrink `decline` to TRUE structural noise (TOC / chapter headers / fragments); everything else is classified + kept.

## 3. Phase + verify
- [ ] 3.1 Phase 1: Rule (subsumes extraction-cross-type-recovery's rules-policy). Then Lore/Variant.
- [ ] 3.2 Tests + offline decline/category deltas on the corpus; targeted re-extract confirms rules/lore now classified, not declined.
