> **PARKED — investigation record.** Captures the agreed direction; to be refined on resume (see proposal.md).

## Context

The structured-entity pipeline today is **type-first**: a keyword classifier (`HeadingCategoryClassifier.GuessCategory`) assigns a `ContentCategory` from a heading/bookmark title; the LLM is then handed that type's JSON schema and told to extract. Failure mode: a wrong type isn't rejected — the LLM fills the schema anyway, fabricating data. Entities are flat `entity{type, fields, canonicalText}` records embedded into `dnd_entities`. Prose chunks live separately in `dnd_blocks` (17,571 chunks) and are reliable.

The Dragonborn case proved both the misclassification (PHB + SRD both store Dragonborn as fake `Monster`s) and the abstraction gap (the breath-by-ancestry table can't be represented and was lost), while the **prose retained the full table verbatim**.

## Goals / Non-Goals

**Goals (direction):**
- Structured facts are **grounded in prose** (provenance pointer) and **validated** — never fabricated.
- A model that can represent **tables, choice-sets, nesting, and relationships**.
- Scope driven by the **companion's real queries**, proven on one vertical slice before corpus-wide rollout.

**Non-Goals (for now):**
- A full corpus re-extraction or 2024-book ingest before the model is proven on a slice.
- Finalizing schemas/requirements — that needs the query-catalog brainstorming on resume.

## Decisions (proposed, not final)

- **Prose = source of truth.** `dnd_blocks` already holds the rich/tabular content cleanly. The structured layer is an *index/overlay* over prose, not a replacement. Every structured fact references the prose chunk(s) it came from.
- **Content-first, reject-don't-fabricate.** Pipeline becomes: read prose → LLM proposes type + structure (incl. "not an entity") → validate against the source text → ground-or-reject to `needsReview`. Removes the keyword-type-guess as the decider.
- **Richer shape than flat entities.** Introduce first-class **tables** (rows of typed cells, e.g. ancestry → damage → breath shape/save), **choice-sets** ("choose one of N"), and **typed relationships** (Class —feature@level→ Feature; Spell ↔ Class; Race —ancestryOption→ row). Nesting preserved.
- **Query-driven model derivation.** Build a catalog of what the companion must answer (character build, encounter design, lore lookup), then derive the minimal model that serves those queries. Avoids "extract every heading."
- **Vertical slice first:** Dragonborn race end-to-end (traits + breath/ancestry table + choice-set + provenance) as the proof, before generalizing.

## Open questions for resume

- The query catalog: what *exactly* must the companion answer? (drives everything)
- Do tables/relationships live in Postgres (relational), Qdrant payloads, or a graph store?
- How much of `dnd_entities` is salvageable vs needs re-extraction? (Dragonborn shows even "trusted" books are affected.)
- Retrieval integration: how do prose RAG + structured overlay + provenance combine at query time (extend `FusedRetrievalService`)?
- Migration: re-extract all 5 books, or only where structured lookup adds value over prose?

## Risks / Trade-offs

- **Scope explosion** — biggest risk. Mitigation: query-driven scope + single vertical slice before any corpus work.
- **Re-extraction cost** — extraction is slow on this hardware (see `project_gpu_extraction_constraint`). A full re-run is multi-day; another reason to scope tightly.
- **Two sources of truth** — must keep structure subordinate to prose (provenance) so it can never silently diverge/hallucinate.

---

## Investigation drill-down (2026-06-24)

Deeper investigation (three parallel subagent probes + control-flow read) that quantifies the failure and converts the agreed direction into a concrete, sequenced architecture. Still PARKED; still no `specs/`/`tasks/`. This section is the durable record of the thinking.

> **The query-catalog brainstorming the proposal deferred is now captured in `query-catalog.md`** (three-domain catalog + bin taxonomy + five pressure-tests + the `CharacterSheet`/engine/storage consequences). §H–J below feed into it; it is the gating requirements map.

### A. The blast radius, quantified

From the actual canonical JSON on disk (`books/canonical/*.json`), not estimated:

- **3,804 entities** corpus-wide across 5 books. Monster-typed: **1,092**.
- **408 / 1,092 Monsters (37.3%)** carry a strong fabrication/misclassification signal — and this is a **lower bound** (only mechanically-detectable impossibilities; a plausible-but-invented stat block is invisible without prose comparison).
  - 256 have a garbage `cr` value: rarity strings (`uncommon`/`legendary`) or spell levels (`cantrip`) sitting in the CR slot — i.e. items/spells force-typed Monster.
  - 164 have `str=dex=con=0` (zeroed-template fabrication).
- **317 of those 408 (77.6%) were marked `needsReview=false`** — silently accepted. This is the headline defect.
- Not Monster-specific: **92 Class-typed** entities have non-class names (`"Spell Slots"`, `"Chapter 3: Classes"`). Cross-type leakage is corpus-wide.
- The pipeline's own error reporting is **blind** to this: the entire corpus produced exactly 2 errors (1 JSON-parse, 1 dangling ref). It only catches parse failures and dangling cross-references — never misclassification/fabrication.

Implication for the parked open question "how much of `dnd_entities` is salvageable?": **not much — re-extraction, not repair.**

### B. The lock, confirmed (and it's more rigid than a tool call)

The control flow that produces the failure: heading → `HeadingCategoryClassifier.Guess` (substring keyword match; `"dragon"` ∈ `"Dragonborn"` → Monster) → page-keyed `TocCategoryMap` (blast radius: every block on the page inherits the category) → `MapCategoryToEntityType` → `candidate.Type` **frozen** → `EntityExtractionOrchestrator.cs:396` selects exactly ONE schema → `OllamaEntityExtractionClient.cs:23` sets `ResponseFormat = ChatResponseFormat.ForJsonSchema(schema)`.

Key finding: **it is not a tool call.** It's grammar-constrained decoding — the model is mechanically incapable of emitting anything but a filled instance of the one locked schema. `emit_<type>_fields` framing is cosmetic; the "EntityType classification rules" in `ExtractionPromptBuilder.cs:62-71` are **dead tokens** the model cannot act on. `needsReview` (`ExtractionNeedsReview.Derive`) is a function of `(name, self-reported confidence)` **only** — it never inspects fields or prose. No grounding gate exists anywhere.

### C. Dragonborn vertical slice — before/after (real data)

Source prose found in the Marker cache (`items[524–543]`, PHB p.35): the full 10-row Draconic Ancestry table is **present and clean** (modulo systematic OCR: `Brealh`, `fI.`, `leveI`). The flat model shattered Dragonborn into **4 records, all typed Monster** (`phb14.monster.draconic-ancestry`, `.dragonborn-traits`, `.draconians`, `.dragonborn-names`), lost the table (became `str:0…cha:0`), and **fabricated** `str:12, cha:11` (source says "+2 Str, +1 Cha" — modifiers, not scores).

The proposed shape, hand-modeled and validated against the prose, needs four constructs the flat `entity{type,fields}` lacks:
- **Table** — typed columns: `ancestry → damageType → breathShape{form,length,width} → save`.
- **ChoiceSet** — "choose one of 10", `binds` the picked row's columns to the Breath Weapon + Damage Resistance traits.
- **Typed relationships** — Race→trait, trait `gatedBy` ChoiceSet, ChoiceSet `drawsFrom` Table.
- **Provenance** — every fact carries a `blockId` pointer to its `dnd_blocks` chunk.

Hard parts surfaced: Marker flattens tables into one OCR-noisy block (per-row provenance impossible); breath **damage** scales by character level (2d6→5d6) while **shape/type/save** scale by ancestry, and the **DMG variant overrides level with CR** (needs a variant mechanism); a resolved choice (Red→fire→cone) belongs in the Hero layer, not the rule entity (pulls retrieval in).

### D. Failure-1 fix — decision: Option C, "C2" flavor

The classification/fabrication fix (Failure 1) is **separable** from the model rework (Failure 2) and is the natural first milestone. Decision: let the model choose its type or decline — **Option C** — implemented as a **discriminated union under the existing grammar-constrained decoding**, not native tool-calling.

- **C1 (native tool-calling)** — register all ~22 `emit_<type>` tools + a decline tool, switch off `ForJsonSchema`. Rejected: abandons the one reliable mechanism on an 8B local model; Ollama tool-selection across a 22-tool surface is where small models fail. This is the "high-risk" option earlier analysis flagged.
- **C2 (chosen)** — one schema that is a `oneOf` over all per-type field schemas **plus a `{entityType:"none", reason}` branch**, fronted by a discriminator; keep `ResponseFormat = ForJsonSchema(unionSchema)`. Delivers C's "pick-or-decline" semantics through the mechanism that already works. `candidate.Type` from the keyword classifier demotes from authority to hint.
- **Open feasibility unknown (gate before committing):** does Ollama/llama.cpp's JSON-schema→GBNF path handle a large `oneOf` discriminated union reliably? Settle with a ~30-min spike (hand-written 3-branch union, one Ollama call, does it decode?). **→ RESOLVED (2026-06-24, change `oneof-decoding-spike`): YES.** On `qwen3:8b` / Ollama 0.30.9, a 4-branch `oneOf`+`const`+decline schema constrained to valid single-branch JSON every call (mechanism PASS), and the model selected correctly — spell→`Spell`, index→`none`, and the Draconic Ancestry block declined to `none` rather than fabricating a `Monster` (capability PASS). **C2 is buildable; no fallback to C1/two-pass needed.** Deferred to the C2 milestone: full ~22-branch union scale (the classifier-as-prior pruning, §F, keeps it small) and confirming a complete Race block types as `Race`. See `oneof-decoding-spike/spike/findings.md`.
- **Scope note:** C is a **Failure-1 fix only** — it makes the corpus *honest* (declines/re-types instead of fabricating) but the Draconic Ancestry table still has nowhere to live until the model rework. C picks the mechanism for honesty; it does not deliver coverage.

### E. The two-gate architecture (the real fix for the 77.6%)

The decline branch alone does **not** close the silent-acceptance defect, because `none` is self-report and 8B models are trained to be helpful → they resist abstaining. Two gates, not one:

- **Gate 1 — decline branch (`none`).** In-band, ~free. Catches *forced* fabrication (where the grammar left no out). Necessary, not sufficient.
- **Gate 2 — grounding gate (independent).** Trusts nothing the model says; checks emitted fields against the source prose. This is what catches the false-accepts (bottom-left of the error matrix) that `none` and `confidence` miss.

**Convergence:** the grounding mechanism *is* provenance — the same primitive the model rework introduces for tables/choices. "Ungrounded" = "can't cite a prose span." Gate 2 (Failure-1 honesty) and provenance-linked structure (Failure-2 coverage) are **one feature seen from two sides**; it is not built twice.

Replace the `needsReview` **boolean** with a **disposition**: `Accepted` (grounded+cited) / `NeedsReview` (ungrounded, or low confidence, or noisy name, or ambiguous decline) / `Declined` (clearly-not-an-entity — **logged, not silently dropped**, so false-declines are auditable) / `Failed` (no valid output).

### F. Classifier-as-prior (cost control with a safety property)

Prune the 22-branch union: make the keyword classifier emit a **ranked set, not a single first-match guess**. Union ≈ `{frequency-floor: Monster/Spell/Item/Class} ∪ {guess + empirical confusion set} ∪ {none}`, ~6–8 branches. The confusion set is grounded in the *actual* misclassifications from §A (Monster↔Race, Monster↔cantrip-Spell, Monster↔rarity-MagicItem, Class↔Rule).

- **Safety property:** because `none` is always offered, a bad prune degrades to a **false-decline, never a fabrication**. This is what makes aggressive pruning safe.
- **Crack in it:** a small model offered a narrow union may anchor and wrongly pick an offered type rather than `none` — another reason Gate 2 is mandatory (never fully trust the model to decline).
- **Self-improving:** log every `(keyword-guess, model-choice, reason)` disagreement → refine the confusion map → union narrows → cost drops. The classifier stops being a brittle authority and becomes a learnable prior.
- **Measurable offline today:** for each of the 3,804 entities, does `{floor ∪ guess+confusion ∪ none}` contain the *corrected* type? Widen until recall ≈ 99%.

### G. The grounding gate's actual check — a tiered cascade

"Field appears in prose" fractures by field type, and OCR noise (`Brealh`, `fI.`) breaks naive matching. No single mechanism wins; cascade cheap→expensive, escalate only the residual. (Infra on hand: embeddings = **mxbai-embed-large**, already computed for `dnd_blocks` chunks, ~free; chat = **qwen3:8b**, 20–65s/candidate — a second call doubles wall-clock.)

- **Tier 0 — OCR-normalized fuzzy match (every field, ~1–10 ms).** Confusable map derived from the real noise (`I→t/l`, `rn→m`); normalize both sides, token-fuzzy-match. Grounds most strings/cells/numbers. Blind spot: semantics and **fine facts** — cannot discriminate Black→acid from Black→fire (shared tokens).
- **Tier 1 — embedding coarse type-grounding (1×/candidate, ~0.1–1 s).** Reuse the existing `dnd_blocks` vector. Kills *gross* fabrication ("Feywild rules paragraph → Monster" scores low against a stat-block anchor). Blind spot: grounds **topic, not fact** (cos("Black:acid") ≈ cos("Black:fire")).
- **Tier 2 — qwen3 LLM-judge (residual only, +20–65 s).** Handles every field incl. fine cells, reads through noise. Expensive + correlated failure (model checking model). Run ONLY on candidates failing Tier 0/1 or high-stakes table cells.

Amortized: if Tier 0+1 clear 80–90%, cost ≈ `extract + ~15–17%`, vs `+100%` for judge-everything — **hours not days**. The driver is the **escalation rate**, which is measurable offline against the 3,804 entities before any commitment.

**Resolving the table problem:** the cell the companion most needs (Black→acid) is exactly what cheap grounding *cannot* verify and only the expensive correlated judge can. The resolution is **not** to auto-prove every cell — it's: point the cell at its prose span via provenance, let cheap grounding confirm the span is on-topic + tokens present, and route fine-grained/low-confidence tabular extractions to the **human review queue**. The gate's contract is "catch gross fabrication cheaply, never silently accept the uncertain" — not "prove every fact." That contract is what makes this buildable on an 8GB laptop.

### Sequencing that falls out

1. **Spike** the `oneOf` feasibility (§D) — gates everything.
2. **Milestone 1 — Option C2 + two-gate honesty** (§D–G): content-first pick-or-decline, prior-pruned union, grounding cascade, disposition enum. Makes the corpus honest; gives a clean correctly-typed candidate stream. Stands on its own.
3. **Milestone 2 — richer model** (§C): tables/choice-sets/relationships/provenance, proven on the Dragonborn slice. Delivers coverage. Reuses the provenance primitive from Gate 2.

### H. The read path — structure only matters if query-time exploits it

Threads A–G are all **write-path** (extract → ground → store). This is the missing **read-path** half, and it reframes the effort. Grounded in current `FusedRetrievalService` + the MCP tools (`search_entities`, `get_entity`, fused search).

**Surprise finding — the current read path doesn't exploit structure.** `FusedRetrievalService.FetchEntitiesAsync` retrieves entities by **embedding similarity on `Envelope.CanonicalText`** and returns that text. The structured *fields* are never used to answer — at query time an entity is mechanically identical to a prose chunk (embed + rerank). The structured layer's only query-time value today is **filtering** (`search_entities` by type/CR/level) and **exact-ID fetch** (`get_entity`). So: *even with perfect extraction, the current read path cannot answer a relational/tabular query by reasoning — only by retrieving a chunk that happens to state it.* Storing the Draconic Ancestry table correctly changes nothing unless the read path does something structural with it.

**The Dragonborn query forces the issue.** *"I'm a level 7 red dragonborn — what's my breath weapon?"* The answer is a **computation**, not a passage: `ancestry=Red → table(Red)={fire, 15-ft cone, Dex save}`, `breathDamage(L7)=3d6`, `DC=8+ConMod+prof`. No single chunk states it; it's assembled from race rules + table + level→damage progression + the character's resolved choice + the character's Con + prof.

**Null-hypothesis steel-man (must beat this):** the chat is already an MCP client with the character sheet. Prose RAG + character context in the prompt + a capable chat model *can* assemble simple "my breath weapon" answers. So structure must win **decisively**, not marginally. It does, in exactly three places:
1. **Aggregation / join across many entities** ("every race granting Con + a resistance"; "level-3 fire spells my Red Dragonborn Sorcerer could learn") — prose RAG retrieves passages, not **sets**; cannot aggregate. Decisive.
2. **Deterministic fine-fact correctness** — a table lookup keyed on `Red` is deterministic; an LLM reading a noisy flattened table can flip a cell (Red→acid). Rules accuracy needs determinism.
3. **Character-projection at scale** — a full build resolves dozens of choice-gated rules every turn; lookups beat re-parsing prose each time.
Where structure does **not** decisively win: narrative/flavor + single-fact lookups (prose RAG suffices). **This sort is the query catalog's job and it drives the model shape.**

**"My breath weapon" is a resolution engine, not retrieval.** The resolved choice (Red) is **character state** (`HeroSnapshot`/`CharacterSheet`); the rule entity holds the **template** (table + "choose one"). Answering = `RAG (find rules)` + `ENGINE (project character.ancestry through table → row; apply character.level to progression; compute DC from Con+prof)`. The engine is *apply rules to a character* — a different operation from RAG. Structure + provenance makes the engine **possible** (key into the table) and **auditable** (every resolved value cites its prose span).

**Read-path twist on the grounding insight (ties back to §G):** to answer "Red dragonborn breath" correctly you must do a structured **lookup** (`key=Red`) — embedding similarity can't discriminate Red→fire from Red→acid (topic not fact). So **fact-precise reads must be structured query (filter/key/join), NOT vector retrieval of `CanonicalText`.** The structured layer needs a real query interface, not just a vector index. The gap in `FusedRetrievalService`: it treats entities as more-text-to-rerank. The new read path is **route**, not merge: relational/structural queries → structured resolver (deterministic); narrative queries → prose RAG; provenance stitches them and enables citation + fallback when a fact is `needsReview`.

**Three co-designed layers (the effort is bigger than "write-path fix"):**
- **write** — content-first extraction + grounding (§B–G) → *honest* structure
- **store** — tables / choices / relationships / provenance (§C) → *representable* structure
- **read** — query router + structured resolver + character-resolution engine → *usable* structure ← newly surfaced, least designed, but the layer that actually delivers the companion goal

**Consequence:** the **query catalog is the true root** — you cannot design the read router, the resolver, or even the model shape without knowing which queries are narrative-vs-filter/aggregate-vs-character-resolution. This is the brainstorming the proposal already defers; this drill shows it gates the read path too, not just extraction scope.

### I. Query catalog (root), storage decision, engine home & the resolved-choice prerequisite

Grounded in: Postgres tables (`Users/Campaigns/Heroes/HeroSnapshots/ChatTurns/Notes/IngestionRecords`; `CharacterSheet` is a JSON column on `HeroSnapshot`), `CharacterSheet` shape (flat fields + a **freeform `Features` list of `{Name,Description}`** — no structured slot for a resolved choice), `dnd_entities` Qdrant store (vector search + payload **filter**, **no joins / no keyed-row lookup**), chat = MCP **client** (`Features/Chat/McpToolsProvider.cs`). These three nest: **catalog → storage → engine.**

**Query catalog — three-way sort by the mechanism each query demands:**
- **Bin A — narrative / single-fact** ("dragonborn culture?", "describe Waterdeep", "what does Fireball do?", "how does grappling work?") → prose RAG over `dnd_blocks`. **Already served; structure adds nothing.**
- **Bin B — filter / aggregate / join** ("all CR-5 flyers", "level-3 spells a Wizard can learn", "races giving a Con bonus", "fire cantrips") → structured **filter + join + set-return**. Prose RAG cannot return *sets*. **Needs structure.**
- **Bin C — character-resolution** ("what's *my* breath weapon?", "what do I get at level 8?", "can I cast Counterspell?", "recommend a feat") → **resolution engine** (apply rules to *this* character). **Needs structure + character state.**

Scoping result: **extract for B and the choice-gated/table-driven slice of C — not "every heading."** Note much of C is *already precomputed* on `CharacterSheet` (`SpellSaveDC`, `ProficiencyBonus`, `ArmorClass`, `SpellSlots`); what's missing is exactly the choice-gated/table-driven content the freeform `Features` list can't hold. **The Dragonborn slice is the canonical Bin-C exemplar** — missing precisely because the sheet has nowhere to put "Red."

**Storage — the read path FORCES Postgres (not a preference call).** §H established fact-precise reads need filter/key/join, and **Qdrant cannot join**. The Draconic Ancestry table *is* a relational table; Spell↔Class *is* a join table; resolved choices already live in Postgres (`HeroSnapshot`).
- **Structured facts → Postgres** (already in stack via `AppDbContext`). Model hot/known shapes as real tables (`entity`, `table`, `table_row`, `relationship`, `choice_set`) + JSONB for long-tail; provenance = FK from each fact to a prose-chunk row.
- **Prose stays in Qdrant** (Bin A narrative RAG — its actual job).
- **`dnd_entities` Qdrant index becomes derived/optional** — a *semantic entry-point* only, never source of truth, never how a fact-precise query is answered. Source of truth = canonical JSON (editable) → projected into Postgres (query substrate) + optionally Qdrant (entry-point).
- **Graph store: NOT now.** Our relationships are shallow (1–2 hops) = Postgres's wheelhouse. Revisit only if a genuinely multi-hop query class appears.

**Engine home — deterministic service behind MCP tools (the §H find-vs-apply distinction decides it):**
- *Not in retrieval* — retrieval finds content; it must not know a character's Con. It fetches the template; it doesn't project.
- *Not in the chat LLM's reasoning* — letting the model project "Red→fire cone, L7→3d6" is the fragile cell-flipping path §H warned against. Rules accuracy demands deterministic code.
- *Yes: a new `CharacterResolutionService`* exposed as MCP tools (e.g. `resolve_character_feature(heroId, "breath weapon")` → `{value, provenance[], confidence}`). The **LLM orchestrates (picks the tool); the engine computes the rule.**

```
chat (MCP client, LLM)  ── orchestrates, doesn't compute rules
   ├─ fused search                   → Bin A (Qdrant prose RAG)
   ├─ search_entities / get_entity   → Bin B (Postgres filter/join)
   └─ resolve_character_*(heroId,…)   → Bin C (NEW CharacterResolutionService)
            └─ load HeroSnapshot (+resolved choices) → fetch templates (Postgres)
               → project choices through tables, apply level/ability → facts + provenance
```

**Provenance → cited answer does triple duty:** (1) **Citation** — each computed component carries a `blockId` → "PHB p.35"; chat renders *"15-ft cone of fire, Dex save, DC 15, 3d6 [PHB p.35]."* (2) **Fallback** — a `needsReview` fact returns its prose span instead of a computed value; the LLM reads it directly (structure when grounded, prose when not). (3) **Audit** — wrong answers trace to a chunk+extraction; fix the data, not the prompt.

**Prerequisite this surfaces:** `CharacterSheet` must store **resolved choices as structured references** (e.g. `ancestry → phb14.choiceset.draconic-ancestry:Red`, chosen subclass features) — not flat fields + freeform `Features`. A scoped piece of work the Dragonborn slice forces.

**End-to-end vertical slice (now fully specified — proves the READ path, not just storage):**
1. Draconic Ancestry table + choice-set in Postgres, each cell w/ provenance FK. *[store]*
2. Resolved-ancestry slot on `CharacterSheet` ("Red" → choiceset ref). *[sheet extension]*
3. `CharacterResolutionService.resolve("breath weapon")` → cited computed fact. *[engine]*
4. `resolve_character_*` MCP tool the chat calls; renders a cited answer. *[read]*

Convergence: **Postgres = structured substrate** (facts + relationships + character state + provenance), **Qdrant = narrative + semantic-entry layer**, **engine = deterministic code behind MCP tools** that joins them and cites its work.

### J. Multiclassing — the composition forcing function (Slice 2)

Multiclassing is **certain to occur**, not an edge case, and it's the deliberate stress test that proves the engine *composes* rules across templates rather than just looking one up. It tightens §I.

**Why it's hard (real 5e rules):**
- **"Level" stops being one number.** Proficiency bonus → *total* character level; a class feature → *that class's* level; Dragonborn breath → *total* level. "What level am I" has three answers depending on what's being resolved.
- **Spellcasting composition.** Slots are not summed — you compute a **combined caster level** (full casters + ½ half-casters + ⅓ third-casters; **Warlock Pact Magic tracked separately**), then look up one Multiclass Spellcaster table; spells known/prepared stay per-class. An aggregate-through-a-special-table, not a lookup.
- **Exception-laden composition:** Extra Attack does **not** stack; multiclassing grants only a *subset* of proficiencies; ability-score prerequisites to multiclass in/out.

**What it BREAKS in §I (so §I was incomplete):**
- `CharacterSheet.Level` is a single `int` — cannot represent Fighter 3 / Wizard 2. The §I "resolved-choice slot" extension is too small: **`Level` must become a per-class breakdown** (`List<ClassLevel>{ class, level, subclass }`), total level *derived*. Bigger, load-bearing sheet change.
- The catalog's Bin-C queries **fork**: "spell save DC / slots" has a single-class form and a multiclass form with a *different resolution path*. The catalog needs a **single-vs-multi composition axis**, not a flat list.
- Engine tools need multiclass-aware resolution (`resolve_spell_slots` must know the combination rule).

**What it VALIDATES (architecture holds, just bigger):**
- **Deterministic engine** — the combined-caster-level arithmetic (rounding, Warlock carve-out) is exactly what an LLM flubs; §I's "deterministic code, not LLM reasoning" becomes non-negotiable.
- **Postgres/relational** — Multiclass Spellcaster table is another relational table; per-class levels are relational state; composition + arithmetic, still no graph-store benefit.
- **Provenance** — multiclass rules are their own prose + table (PHB multiclassing chapter); a multiclass answer cites them alongside per-class sources.

**Slice sequence clarified:**
- **Slice 1 — Dragonborn (racial):** breath scales by *total* level → multiclass-clean. Proves table + choice-set + provenance + engine. Stays the first proof *because* racial features dodge per-class-level.
- **Slice 2 — Multiclass spellcaster:** proves **composition** — per-class levels, the combined-slot rule, the single-vs-multi query fork, the sheet rework. "If the design survives multiclass spellcasting, it survives D&D."

### Open questions added by this drill-down

- `oneOf` feasibility under Ollama structured output (the spike).
- Tier-1 type anchors: what does "looks like a stat block" embed against — per-type exemplars, or field-set-vs-chunk?
- Measured escalation rate (Tier 2 load) against the existing corpus.
- ~~Where the resolved player choice lives~~ **RESOLVED (§H/§I):** `HeroSnapshot`/`CharacterSheet` holds character state; rule entity holds the template; a deterministic `CharacterResolutionService` projects the choice through the table at query time.
- ~~Does the resolution engine live in retrieval, chat/MCP, or a new service?~~ **RESOLVED (§I):** a new deterministic service exposed as MCP tools; LLM orchestrates, engine computes; provenance FK → cited answer with prose fallback.
- ~~Storage: Postgres / Qdrant / graph?~~ **RESOLVED (§I):** structured facts → Postgres (forced by the join requirement); prose → Qdrant; entity vectors → derived entry-point index; graph store deferred.
- **NEW — `CharacterSheet` extension:** how to model resolved choices as structured references (ancestry, subclass features, etc.) without breaking existing flat fields / the freeform `Features` list; migration of existing `HeroSnapshot` JSON. **(§J widens this: `Level` must also become per-class — `List<ClassLevel>{class,level,subclass}`, total derived.)**
- **NEW (§J) — per-class `Level` model:** replace the single `int Level` with a per-class breakdown; derive total level + proficiency bonus; how to migrate existing single-class `HeroSnapshot` JSON.
- **NEW (§J) — single-vs-multi query fork:** the catalog's Bin-C queries that compose across classes (spell slots, save DC) need distinct single-class vs multiclass resolution paths; the engine tool surface must branch on it.
- The query catalog **made real**: a proper enumerated Bin-A/B/C list seeded from the companion's actual feature set, **with a single-vs-multi composition axis (§J)** — the gating brainstorming; drives extraction scope, the read router, AND the engine's tool surface.
