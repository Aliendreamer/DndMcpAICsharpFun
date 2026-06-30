# Required Skill Workflow

Every feature or change MUST follow this exact sequence — no shortcuts:

1. `superpowers:brainstorming` — full dialogue, one question at a time, propose 2-3 approaches, get approval
2. `opsx:propose` — save agreed spec into `openspec/changes/<name>/`
3. `superpowers:writing-plans` — create detailed implementation plan (`plan.md` in the change dir)
4. `superpowers:subagent-driven-development` — execute plan, fresh implementer + reviewer subagent per task

Never collapse to just opsx:propose. Never skip brainstorming dialogue.

**Execute on `main` directly — no feature branches** (single-dev; `mem:workflow/work_on_main`). Commit each reviewed task straight to main; commit autonomy is granted.

**Finish step** (user says "commit" on a done spec): commit → `openspec archive <name> -y` → run `skill-optimizer` → `ingest-entities`. Detail: `mem:workflow/finishing_a_spec`. The canonical, always-loaded version of all this is the **`dev-flow` SKILL** (`.claude/skills/dev-flow/SKILL.md`) — keep it and these memories in sync.

## Validation gates — MUST NOT OMIT (high-signal, learned the hard way)

- **Validate data/extraction changes with a LIVE smoke run BEFORE trusting them.** Green unit tests are necessary, NOT sufficient — this cycle 790 green tests still shipped a Dragonborn→Monster regression. Run on a real book, inspect the actual output (type counts, names, the reject/declined file) before declaring success or ingesting.
- **Clear the conversion cache after a converter-logic change.** The MinerU disk cache (`books/conversion-cache/*.mineru.json`) is keyed by PDF hash ONLY — converter changes are not reflected. `docker exec … rm -f` it before the smoke, or a "cache hit" at run start means you're testing the OLD mapping. (Cost a wasted full run.)
- **Early spot-check BEFORE the full ~8 h run.** First checkpoint (`<slug>.progress.json`, ~first 100 candidates / ~15 min) covers front-of-book entities (races, classes) — spot-check the entities your change TARGETS there first, instead of waiting hours.
- **A count change is NOT automatically a regression — diff WHICH entities changed type.** Monster 34→30 this cycle was a CORRECTION (Acolyte/Noble/Sage/Soldier were mis-typed backgrounds moving to Background), not a loss. Compare before/after per-entity type-maps; never assume a drop is bad.
- **Before specifying a predicate over a data shape, CHECK the real shape first.** `EntityCandidate.TypePrior` always carries the scanner's frequency floor `{Monster,Spell,Item,Class}` — an "all priors gated" predicate could never fire. Read the producer (`HeadingCategoryClassifier.ExpandPrior`) first.
- **When recall looks low, distinguish OUR-logic bugs from UPSTREAM gaps.** If a missing entity is NOT in `declined.json`, it was never a candidate → upstream (parser/scanner) gap, not our gate. Beyond declined: a valid candidate that vanishes with NO trace (no entity/decline/error/log) = a downstream scanner/orchestrator silent-drop (`mem:project_extraction_recall_fixes`).
- **For a SINGLE missing entity, hand-author the canonical** (`books/canonical/<slug>.json`, root-owned → `docker cp`) — the designed escape hatch, cheaper than a 3rd parser/injector patch.
- **A reviewer's "verified against the diff" is not enough for behavioral claims** — confirm against LIVE code/producer and real output.

## Speed / tooling (this pipeline)
Extraction is ~30s/candidate, ~8.5 h/book — qwen3 runs with thinking ON. `think:false` ≈ 4-5× faster (not yet applied). No page-range/chapter-scoped extract yet (full-book only) — that + think:false are the fast-iteration tooling gaps; early-checkpoint spot-checks are the current substitute.

## skill-optimizer note
The dev-flow SKILL pack now EXISTS (`.claude/skills/dev-flow/SKILL.md`). Without an eval harness, the finish-step "skill-optimizer" run = apply its salience heuristics (few high-signal rules, fragile behaviors in top-level checklists, explicit must-not-omit wording) to keep the SKILL + these memories tight and current — and fix any STALE facts (this cycle: the SKILL still said "Parser = Marker"; corrected to MinerU).
