> **STATUS: SUPERSEDED / DELIVERED (2026-07-17).** This investigation's agreed direction was implemented piecemeal by later shipped changes — it never needed its own `specs/`/`tasks/`. Closing as delivered, not implementing.
>
> **Where each piece landed:**
> - **Milestone 1 (§D–G) — content-first honesty:** shipped as `content-first-extraction`, `discriminated-union-extraction-decoding` (the `oneOf` C2 union), `entity-grounding-cascade` + `extraction-grounding-gate` (the tiered cascade §G), `extraction-disposition` (the disposition enum §E), `deterministic-type-resolution` (classifier-as-prior §F). Further hardened by `extraction-authority-ladder` (subclass roster, `IsRealEntity`, authority labels) and `extraction-noise-name-gate`.
> - **Store (§C/§I) — structured facts in Postgres:** `Domain/StructuredFacts.cs` (`StructuredTable`/`StructuredTableRow`/`ChoiceSetRow`) + migration (`structured-knowledge-store`).
> - **Read/engine (§H/§I) — deterministic resolution + provenance:** `Features/Resolution/CharacterResolutionService.cs` (`ResolvedComponent` carries `ProvenanceRef`) exposed via MCP (`character-fact-resolution`).
> - **Slice 1 (Dragonborn breath weapon) — the canonical proof:** `ResolveBreathWeaponAsync` (ancestry choice → structured table → cited fact). DONE.
> - **Slice 2 (§J multiclass composition):** per-class `ClassLevel` + multiclass validity + forked caster spell-slot resolution (`multiclass-character`, `hero-multiclass-editing`, `character-*` coach suite).
>
> Any *breadth* beyond the two proof slices (full corpus-wide prose-grounded re-extraction, a complete Bin-A/B/C query router) was intentionally NOT part of this change's mission ("prove one vertical slice first") and would be separate follow-on work if ever wanted.
>
> ---
> _Original parked note (2026-06-17): captured findings + agreed direction from the SRD investigation; deferred `specs/`/`tasks/` pending the query-catalog brainstorming._

## Why

The SRD extraction (1,494 entities) exposed that the **structured-entity extraction layer is unreliable corpus-wide and built on the wrong abstraction** — not an SRD-specific or bookmark-specific problem.

**Evidence (the Dragonborn worked example):**
- A companion needs a Dragonborn's racial traits + breath-weapon-by-ancestry (Black→acid 5×30 line, Red→fire 15-ft cone, …).
- That table is present **verbatim and clean in the prose** (Marker markdown → `dnd_blocks`).
- But in **both** the SRD *and* the "trusted" PHB, the entity layer **misclassified** "Dragonborn Traits"/"Draconic Ancestry" as `Monster` and the LLM **fabricated a stat block** instead of capturing the table. No entity anywhere in the 2,309-entity corpus holds the breath/ancestry data.
- So the information **dies at extraction, not conversion**: the entity step took good prose and *replaced it with hallucinated garbage*.

**Two systemic failures the investigation found:**
1. **Misclassification + fabrication, corpus-wide.** Type-first design (guess type by keyword → fill that type's schema) means a wrong guess becomes convincing fabricated data, with no link back to prose to expose it. Present in bookmark-derived books (PHB) too, not just the heading fallback.
2. **Wrong abstraction.** D&D content is nested, tabular, relational, and choice-laden; the flat `entity{type, fields}` shape literally cannot represent breath-by-ancestry tables, class-features-by-level, "choose one of N", or spell↔class relationships.

**SRD overlap analysis (supporting context):** of 1,494 SRD entities vs the 4 existing books — 28% exact duplicates, 38% misclassified (wrong type), 35% "new" that is ~all garble/cascade-misclassification; genuine net-new value ≈ a dozen items (a few conditions, 2–4 deity-pantheon blobs). The SRD was correctly **shelved**.

## What Changes (direction, to be designed on resume)

Re-architect the structured layer around one principle and a richer model:

- **Principle:** *prose is the source of truth; structure is a validated, provenance-linked derivative that may decline ("needsReview") but must never fabricate.*
- **Content-first extraction:** read prose → propose structure → validate against the source → reject-to-review if it doesn't ground (replaces keyword-type-guess → schema-fill).
- **Richer model:** nested entities + typed **relationships** + first-class **tables** and **choice-sets**, every structured fact carrying a pointer to its source prose chunk.
- **Query-driven scope:** derive the model from what the companion must answer (build a character, design an encounter, answer lore) — not "every heading in every book."
- **Prove on one vertical slice first** (the Dragonborn race, end-to-end) before re-extracting the 2,309-entity corpus or touching the 2024 books.

## Capabilities

### New Capabilities
<!-- Deferred to resume. Likely: prose-provenance-linking, content-first-validated-extraction, knowledge-tables-and-relationships, query-driven-extraction-scope. To be derived from the query catalog. -->
- _(to be defined on resume)_

### Modified Capabilities
<!-- Likely heavy impact on entity-extraction-pipeline, structured-entities, llm-extraction, hybrid-retrieval. To be assessed on resume. -->
- _(to be defined on resume)_

## Impact

- Foundational: touches the core data model (`Domain` entity types, canonical JSON schema), the extraction pipeline, retrieval, and likely a re-extraction of all books.
- Combines three earlier framings (the user's words: "combination of the 3 and wrong abstraction"): (1) **shrink** structured extraction to where it's reliable + lookup-valuable; (2) **fix corpus-wide** with content-first + validation; (3) **replace the abstraction** with nested/relational/table/choice-set + provenance.
- Relates to the heading fallback (`heading-derived-toc-fallback`, shipped) — that's now seen as a corner of this larger problem.
