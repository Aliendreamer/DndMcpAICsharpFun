# D&D Companion — Roadmap & Progress (living doc; update markers as we go)

**North star:** a D&D companion agent that REASONS (character build / encounter design / setting-aware lore), not just retrieves. RAG + extraction are the *means*.

## Status legend: ✅ done · 🔄 in progress · ⬜ not started

## Track 1 — WRITE-layer extraction quality  ✅ (recall + precision shipped, validation running)
- ✅ `consolidate-extraction-signatures` archived (deterministic-type-resolution spec synced).
- ✅ MM/PHB/DMG re-run on the resolver pipeline; `playerhandbook-2014.json` orphan removed; `phb14.json` is the slug.
- ✅ **Recall fix** (`extraction-name-resolution`, MERGED main `e80976a`): 5etools `EntityNameIndex`/`EntityNameMatcher` as resolver step 1 (deterministic KEEP/CLEAN/TYPE, never DROP); `IsEntityLikeName` all-caps over-reject → structural-denylist + lair-reject; `RecordedEntityId` unifies entity id across checkpoint/retry/extract. Recovered Bard→Class, appendix animals→Monster, clean canonical names. (Subsumed Item-4 Aboleth-MIA + lair-filter follow-ups.)
- ✅ **Precision fix** (`extraction-authoritative-allowlist`, MERGED main `a78aab9`): for OFFICIAL books (have `FivetoolsSourceKey`), decline a candidate whose PRIMARY prior type is gated {Spell,Monster,Class,Race,Background,Feat,Condition,God} and that doesn't match 5etools — no LLM call — recording to `<slug>.declined.json`. Kills the chapter-body noise (prior PHB had 397 bogus "Class"; PHB has 12). Stat-block + magic-item rescues precede the gate. Found+fixed a critical bug mid-build: gate must key off PRIMARY prior, since the scanner's frequency floor always appends ungated Item.
- 🔄 **LIVE PHB validation running** (on main, container rebuilt): confirm Class 397→~12, race fields gone, `declined.json` populated, recall intact. THEN archive both changes (`openspec archive`) + `ingest-entities` the corrected canonical. (qwen3 ~55 tok/s, run is a few hours — see Extraction performance below.)

## Item 2 — Slice 2: multiclass spellcaster  ⬜ (next big build, RECOMMENDED)
The design's deliberate stress test — "if it survives multiclass spellcasting, it survives D&D." Extends slice-1 rails (`CharacterResolutionService`, structured-fact store, `resolve_character_feature` MCP tool).
- `CharacterSheet.Level` int → `List<ClassLevel>{class,level,subclass}`, total derived.
- Combined-caster-level slot math (full + ½ + ⅓ casters; Warlock Pact Magic separate) → one Multiclass Spellcaster table.
- Bin-C queries FORK: single-class vs multiclass resolution paths.
- Design home: `openspec/changes/prose-grounded-knowledge-model/design.md` §J.

## Item 3 — Auto-NeedsReview grounding cascade  ⬜ (own brainstorm→spec)
Tier 1 = embedding check (reuse mxbai dnd_blocks vectors → promote); Tier 2 = qwen3 judge on the residual (promote/decline/keep, bias decline-when-unsure). Reuse the `errorsOnly` re-run as a "re-ground" pass. Inter-book dangling-ref warnings are auto-fixable via a corpus-wide re-resolution after all books ingest. Can now ALSO re-examine `declined.json` records (false-drop audit). Cannot auto-PROVE fine cells (Red→fire) — stay needsReview+provenance.

## Item 4 — Corpus-wide dedup  ⬜ (separate step, AFTER Item 1 validation)
The allowlist gives deterministic canonical names+types, so a cross-source entity can appear once per book. Add an explicit dedup pass (by canonical id / `EntityNameIndex` key), OUT of the extraction path (separate, reviewable). Gated on the live validation landing.

## Extraction performance  ⬜ (perf, not correctness)
- **Disable qwen3 thinking for extraction** — qwen3:8b is a hybrid reasoning model; by default it emits `<think>` chain-of-thought before the structured answer, generated at the same ~55 tok/s (8 GB GPU ceiling) then discarded. Extraction is schema-filling from candidate text → rarely needs reasoning. Pass ollama `"think": false` (or `/no_think`) on extraction requests → ~1.5–3× fewer generated tokens. **We never configured this** (no `think`/`no_think` in the extraction client today). MEASURE speed AND field-correctness on a sample before committing (thinking may help ambiguous candidates). RELEVANT MAINLY FOR FULLY-LOADED (dense, fully-GPU-resident) models like qwen3:8b where raw generation token-rate is the bottleneck; a partially-offloaded MoE (`--cpu-moe`, Item 5 in `mem:project_companion_roadmap`) has a different cost profile.
- The allowlist gate already cut total LLM calls ~4× (noise declined). Bigger model-tier lever = local-model investigation (Gemma-4-26B-A4B / Qwen3-30B MoE) — see the file-memory roadmap Item 5.

## How we progress (the discipline — never skip)
Every item: **superpowers:brainstorming** (full dialogue) → **opsx:propose** (spec in `openspec/changes/<name>/`) → **superpowers:writing-plans** → **superpowers:subagent-driven-development** (per-task TDD + reviewer subagents, final whole-branch review on opus). **Work DIRECTLY on main — no feature branches** (single-dev; `mem:workflow/work_on_main`); commit autonomy granted. FINISH: when the user says "commit" on a done spec → commit → `openspec archive` → run `skill-optimizer` (`mem:workflow/finishing_a_spec`).

## Current position (2026-06-28)
Both extraction-quality changes (recall + precision allowlist) MERGED to main, build clean, 779/779 non-persistence green. Live PHB re-run validating the gate at scale (qwen3 GPU-bound, a few hours). Next once validated: archive both + ingest corrected canonical; then **Item 2 (slice 2 multiclass)** is the recommended next build. Slice 1 (`character-fact-resolution`) shipped earlier (`235699a`). Relates to [[extraction_pipeline_state]], and the fuller file roadmap `project_companion_roadmap`.
