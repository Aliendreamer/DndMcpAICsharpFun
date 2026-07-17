> **STATUS: PARKED — brainstorming output (2026-06-24).** The query catalog the proposal deferred ("resume with the query-catalog brainstorming"). Scope decided in dialogue: **all three companion domains, deep**, with the **build kept incremental** (Dragonborn racial → multiclass spellcaster slices). This is the requirements MAP, not a build order. Five pressure-tests run against the synthesis (each tightened it). Companion to `design.md` §A–J. Still no `specs/`/`tasks/`.

## Why this artifact

The whole effort kept converging on one root: *what exactly must the companion answer?* This enumerates it. It drives three things at once: **extraction scope** (what to pull from books), the **read router** (prose vs structured vs engine), and the **engine/tool surface** (what MCP tools + resolvers exist).

## The bin taxonomy

Every query sorts into one of four bins by the *mechanism* it demands:

- **Bin A — narrative / single-fact** → prose RAG over `dnd_blocks` (Qdrant). **Already served; structure adds nothing.**
- **Bin B — filter / aggregate / join** → structured query (Postgres). Prose RAG cannot return *sets*. **Needs structure.**
- **Bin C — resolution** → deterministic engine: apply rules to a stateful subject. **Needs structure + subject state.**
- **Bin D — recommendation / planning / generation** → LLM judgment layered **on B/C facts** (never raw prose).

Two axes cross-cut the bins:
- **single ↔ multi** (concentrated in Bin C; every multi point is a **multiclass** forcing point — see §J).
- **validatable ↔ not** (splits Bin D — see "Bin D splits" below).

---

## Domain 1 — Character Build & Resolution

**Bin A:** "dragonborn culture?", "how does Rage work?", "Circle of Spores flavor" — prose.

**Bin B:** races giving a Con bonus · Wizard subclasses · level-3 fire spells · which classes cast Counterspell (**join** Spell↔Class) · martial piercing weapons · feats with no prereq.

**Bin C (✦ = multiclass forcing point):**
| Query | Needs (char state → rule data) | single/multi |
|---|---|---|
| "What's my breath weapon?" | ancestry choice → ancestry table + level scaling + Con | single (racial; multiclass-clean — **Slice 1**) |
| "My spell save DC?" | class + casting ability + prof | per-class |
| "What spell slots do I have?" ✦ | per-class levels → combined caster-level rule + slot table | **multi (Slice 2)** |
| "Can I cast Counterspell now?" | known/prepared + slots + class list | per-class |
| "What do I get at level 8?" ✦ | which class's level → features-by-level | **multi** |
| "Feats I'm eligible for?" | ability scores + prereqs | single |
| "AC in plate + shield?" | equipment + proficiency + Dex cap | single |
| "Does Extra Attack stack?" ✦ | per-class features → non-stacking rule | **multi** |
| "Can I multiclass into Paladin?" ✦ | ability scores → multiclass prereqs | **multi** |

**Bin D:** recommend a feat · optimize a spell list · plan a build to level 10 (sequential) · pick a subclass for a concept.

> The Bin-C "Needs" column is effectively a spec for what `CharacterSheet` must hold structurally (resolved choices + per-class levels). The catalog doubles as the sheet requirements.

---

## Domain 2 — Encounter Design (DM-side)

**Bin A:** beholder tactics · how lair/legendary actions work · scene flavor.

**Bin B (heavy):** all CR-5 monsters · undead in forests (type + **environment tag**) · flying monsters under CR 3 · creatures with Pack Tactics · CR 10–15 with legendary actions. *Extends to:* traps by tier/type, hazards by environment, treasure hoards by CR tier (**CR-keyed table lookup**).

**Bin C-analog (subject = the PARTY):** "is this group balanced for my party?" · "XP budget for a *hard* encounter?" · "how much XP is this fight worth?" · "is this trap deadly for my party?" · "how does this blizzard affect my party?"

**Bin D:** build a balanced encounter (constraint search) · suggest a boss+minions · scale for 6 players · suggest a magic-item reward (reuses Domain-1 item filter).

**Encounter math (the concrete rules):**
```
PARTY side:    fold(level → threshold) + size-bracket   (≤2 / 3–5 / 6+)
MONSTER side:  fold(CR → XP)           + count-bracket   (1 / 2 / 3–6 / 7–10 / 11–14 / 15+)
budget fn:     (party_levels[], monster_xp[], monster_count, party_size) → difficulty
```
- Party of **3/4/5 are all the same bracket** → only the summed budget differs (pure aggregation); the emergent multiplier is constant in 3–5. Non-decomposable behavior lives only at the **bracket boundaries** (≤2 bumps harder, ≥6 bumps easier). Test matrix: party of 4 (trivial) + party of 2 + party of 6 (exercise the steps).
- **Generation** ("build an encounter") is a **coupled constraint search**: pick monsters + counts to hit a target band, where count couples into the multiplier (count → multiplier → effective-XP). Variable cardinality ("n+1 enemies"). Rule-validatable → grounding gate applies.

---

## Domain 3 — Lore / Setting-Aware

**Bin A (dominant):** "who is Lolth?" · "describe Waterdeep" · "history of the Sword Coast" · faction beliefs.

**Bin B (the structured *facets* of setting entities):** deities by domain/alignment · factions in a location (**join** Faction↔Location) · planes by category. *The description is Bin A; the alignment/domain/membership/taxonomy facets are Bin B.*

**Bin C-analog (subject = the CAMPAIGN):** "in *my* campaign, who rules the city?" (homebrew **overrides** canon) · "is this deity active in my setting?" (setting scopes the answer).

**Bin D (most prominent here):** generate a tavern + plot hook · create an NPC priest of Lathander · rumor table for a city.

---

## Cross-domain synthesis (five pressure-tests)

### S1. One stateful subject = the CHARACTER
The resolution engine is a deterministic pattern: `(subject state, rule-tables) → computed fact + provenance`.
- **Engine unification, tested twice and simplified twice:** "one engine, three subjects" → "engine (2 subjects: character, party) + retrieval + precedence" → **one stateful subject (character)**.
- **Party is NOT a subject** — it's `fold over characters + a thin collective bracket rule` (encounter budget). Its "state" `(count, levels)` is *derived*, not persisted.
- **Encounter is NOT a subject** — symmetric to party: `fold over monster-refs + count-bracket`. Monsters are static reference data (+ multiplicity/templates = composition state).
- **Campaign is NOT the engine** — it's a retrieval-scoping + homebrew-override concern (belongs with the read router).

### S2. Two recurring primitives
- **Tables** — ancestry, slot table, treasure-by-CR, trap damage. Table-keyed lookup is load-bearing in *every* domain (confirms §C first-class tables).
- **Template / instance** — choice-set (template) vs resolved choice; monster stat block vs N instances; **NPC definition vs N appearances**. D&D is "reusable definition + instantiated play-state" at every level.

### S3. Bin D splits by VALIDATABILITY (the load-bearing split)
| Sub-class | Consumes | Validatable? | Engine need | Build when |
|---|---|---|---|---|
| **D-select** (single) | B set + C current | ✅ eligibility | current-state resolve | early |
| **D-plan** (sequential) | B + repeated C **what-if** | ✅ legal at each step | engine as **pure fn over hypothetical state** | deferred; *design for now* |
| **D-generate** | setting facts + flavor | ⚠️ consistency only | none (prose) | independent |
- **The grounding gate extends to recommendations** — refuse to recommend an ineligible feat / illegal multiclass / mis-budgeted encounter, the same way extraction never fabricates.
- **D always consumes B + C, never raw prose** — recommendation quality is capped by structured-fact reliability everywhere.

### S4. Source-precedence is the real cross-cutting service (not "one engine")
Canon vs campaign-homebrew, with override precedence. Consumed by **both** the engine (homebrew can override a racial trait → the *hard* engine must apply it) **and** retrieval (setting-aware grounding). Provenance generalizes from "which book" to "which source: canonical book OR campaign homebrew." Applies to facts *and* NPCs.

### S5. NPCs reuse the engine and stitch all three domains
- **Fidelity dial:** flavor-only (prose) → monster-style (stat block / reference data) → **full-build (a character subject → reuse the engine)**.
- An NPC build = **Bin-D (choose) → engine (resolve)**. The engine is **owner-agnostic** (PC or NPC). The same Bin-D-select spell-rec tool serves PCs and NPCs.
- **Reuse across campaigns/encounters/adventures** forces: a **definition/instance split**, a **scope tier** (campaign-local / DM-library / canon), stable IDs, and source-attribution.
- Loop closes across domains: lore *generates* the NPC → engine *resolves* its stats → encounter *drops it in* as a member (needs **effective CR** — a fuzzy DMG conversion, flagged not solved).

---

## Consequences for the model & existing code

1. **`CharacterSheet` must change** (today: flat `int Level` + freeform `Features{Name,Description}` + mixed definition/runtime fields on `HeroSnapshot`):
   - **Per-class levels** — `List<ClassLevel>{class, level, subclass}`, total derived (multiclass, §J).
   - **Resolved choices as structured refs** — e.g. `ancestry → choiceset:Red` (engine projection, §I).
   - **Split definition vs runtime** — definition (Race/Class/Levels/SpellsKnown — what the engine resolves) vs instance state (CurrentHP/UsedSlots/conditions — per appearance). Reuse forces this.
   - **Ownable beyond a player Hero** — scope tier so NPCs (DM-library/canon) reuse the same character-state.
2. **Engine = pure function** `(state, rule-tables) → fact`, callable on *hypothetical* states (D-plan what-if) and *any* owner (NPC). Do not hardwire to the persisted sheet.
3. **Storage (confirmed §I):** structured facts + relationships + tables + character/NPC state + provenance → **Postgres**; prose → Qdrant; entity vectors → derived entry-point index; graph store deferred (shallow joins).
4. **Read path = route, not merge:** Bin A → prose RAG; Bin B → Postgres filter/join; Bin C → resolution engine (MCP tools); Bin D → judgment layer over B/C. Source-precedence service consulted throughout; provenance → cited answers with prose fallback when `needsReview`.

## Build order (incremental; the catalog is the map, not the order)
1. **Spike** `oneOf` feasibility (gates the C2 extraction fix, §D).
2. **Slice 1 — Dragonborn (racial):** table + choice-set + provenance + engine; multiclass-clean.
3. **Slice 2 — Multiclass spellcaster:** per-class levels, combined-slot rule, single-vs-multi fork, sheet rework.
4. Later slices: encounter budget (party+encounter folds), NPC build/reuse, lore facets, Bin-D.

## Open questions (carried + new)
- Granularity: Domain 1 approved at first-cut depth, may deepen later; Domains 2–3 at first-cut depth.
- Effective-CR conversion for full-build NPCs dropped into encounters (fuzzy DMG process).
- Definition/instance + scope-tier schema for character-state; migration of existing single-class `HeroSnapshot` JSON.
- Where the resolution engine + source-precedence service physically live (new services behind MCP tools).
- Carried from `design.md`: `oneOf` feasibility, Tier-1 grounding anchors, escalation-rate measurement.
