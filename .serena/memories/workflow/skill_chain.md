# Required Skill Workflow

Every feature or change MUST follow this exact sequence — no shortcuts:

1. `superpowers:brainstorming` — full dialogue, one question at a time, propose 2-3 approaches, get approval
2. `opsx:propose` — save agreed spec into `openspec/changes/<name>/`
3. `superpowers:writing-plans` — create detailed implementation plan (`plan.md` in the change dir)
4. `superpowers:subagent-driven-development` — execute plan, fresh implementer + reviewer subagent per task

Never collapse to just opsx:propose. Never skip brainstorming dialogue.

**Execute on `main` directly — no feature branches** (single-dev; `mem:workflow/work_on_main`). Commit each reviewed task straight to main; commit autonomy is granted.

**Finish step** (user says "commit" on a done spec): commit → `openspec archive <name> -y` → run `skill-optimizer` → `ingest-entities`. Detail: `mem:workflow/finishing_a_spec`.

## Validation gates — MUST NOT OMIT (high-signal, learned the hard way)

- **Validate data/extraction changes with a LIVE smoke run BEFORE trusting them.** Green unit tests are necessary, NOT sufficient. This cycle a smoke caught a gate that was structurally DEAD in production while all tests passed. For any extraction/pipeline change: run it on a real book, inspect the actual output (type counts, names, the reject/declined file) before declaring success or ingesting.
- **Before specifying a predicate over a data shape, CHECK the real shape first.** This cycle the gate used `TypePrior.All(gated)`, but the scanner's frequency floor ALWAYS appends ungated `Item` → the predicate could never fire. Read the actual producer (e.g. `HeadingCategoryClassifier.ExpandPrior`, `EntityCandidate.TypePrior`) before writing the condition; don't assume the shape from the name.
- **When recall looks low, distinguish OUR-logic bugs from UPSTREAM/parser gaps.** Check the reject/declined audit: if a missing entity is NOT in the declined file, it was never a candidate → it's an upstream (parser/scanner) gap, not our gate. This cycle proved the 8 missing classes / 8 missing races were a Marker candidate-gen gap (→ `mem:project_parser_upgrade_mineru`), not a false-drop — saved chasing the wrong layer.
- **A reviewer's "verified correct against the diff" is not enough for behavioral claims** — confirm against the LIVE code/producer and, for data pipelines, against real output.

## skill-optimizer note
skill-optimizer is a benchmark-driven SKILL-PACK optimizer (with/without-skill eval loop). To get its full value each cycle, we'd need (a) a DndMcpAICsharpFun-specific dev-flow SKILL pack (this workflow as a SKILL.md, not just memory) and (b) a small eval harness. Until then, the finish-step "skill-optimizer" run = apply its salience heuristics (few high-signal rules, fragile behaviors in top-level checklists, explicit must-not-omit wording) to tighten these workflow memories — as done here.
