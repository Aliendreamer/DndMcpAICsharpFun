---
name: dev-flow
description: Use when starting, implementing, or finishing any feature/change in DndMcpAICsharpFun (the single-host .NET 10 D&D RAG + MCP + Blazor app) — at feature start, before writing code, and before claiming any change done, green, or validated.
---

# Dev Flow (DndMcpAICsharpFun)

## Overview

The repeatable cycle for changing this project: one ASP.NET Core .NET 10 host serving API + MCP + Blazor UI, with the ingestion/extraction → Postgres/Qdrant pipeline. Each change runs the same loop and **lands directly on `main`** (single-dev, no feature branches).

**Core principle:** No change is "done" on assertion. Done = gates run, output seen, all green. For data/extraction changes, done = **validated on a real book**, not just unit-green. Evidence before claims.

## When to Use

- Starting any feature/change/spec.
- Before writing implementation code.
- Before claiming a change is "done", "fixed", "working", "green", or "validated".
- Before committing or archiving an OpenSpec change.

## The Cycle (never skip, never collapse)

1. **Brainstorm** (`superpowers:brainstorming`) — full dialogue, one question at a time, propose 2-3 approaches, get explicit approval. Design is captured in the OpenSpec change, never loose `docs/`.
2. **Propose** (`opsx:propose`) — create the OpenSpec change in `openspec/changes/<name>/` (proposal, design, specs, tasks).
3. **Plan** (`superpowers:writing-plans`) — detailed bite-sized plan saved to `openspec/changes/<name>/plan.md`.
4. **Implement** (`superpowers:subagent-driven-development`) — fresh implementer + reviewer subagent per task, per-task TDD, fix loop on Critical/Important, final whole-branch review on the most capable model. **All on `main`** — commit each reviewed task straight to main (commit autonomy granted; `mem:workflow/work_on_main`).
5. **Finish** (user says "commit" on a done spec): **commit → `openspec archive <name> -y` → run `skill-optimizer` → `ingest-entities`** (`mem:workflow/finishing_a_spec`).

## Serena is MANDATORY for code

All `.cs` reads/edits go through Serena MCP (`find_symbol`, `replace_symbol_body`, `replace_content`, `find_referencing_symbols`). Built-in Read/Edit/Write on code files is **forbidden** (`mem:workflow/serena_first`). Every implementer/reviewer subagent prompt must include the CRITICAL-Serena block + an `initial_instructions` call.

## Quality Gates (must be GREEN before "done")

```bash
dotnet build                                          # 0 warnings (warnings-as-errors via Directory.Build.props)
dotnet test --filter "FullyQualifiedName!~Persistence"  # non-persistence suite (no DB needed)
dotnet test                                           # FULL suite — needs Docker (Testcontainers postgres:18)
dotnet format --verify-no-changes                     # formatting/analyzers
pnpm lint:md                                          # markdown: 0 error(s) (after pnpm lint:md:fix); see /lint-md
```

- Persistence tests need Docker running (Testcontainers + Respawn). Non-persistence suite needs nothing.
- Endpoint change (`MapGet/Post/Put/Delete`) → update `DndMcpAICsharpFun.http` AND `dnd-mcp-api.insomnia.json` in the same commit.

## Validation gates for DATA / EXTRACTION changes (must-not-omit — learned the hard way)

Unit-green is necessary, NOT sufficient for pipeline changes. Before claiming an extraction/scanner/resolver/gate change works:

- [ ] **Run a LIVE smoke on a real book** (`extract-entities` on PHB), then inspect the actual output — type counts, names, AND the reject/`declined.json` audit — before trusting or ingesting. (A green-tested gate was structurally DEAD in production this cycle; only the live run caught it.)
- [ ] **Clear the conversion cache after a converter change** (`docker exec … rm -f /books/conversion-cache/*.mineru.json`) — else the smoke runs the OLD mapping. A "cache hit" log at run start when you changed the converter means your fix is NOT being tested.
- [ ] **Early spot-check BEFORE the full ~8 h run.** The first progress checkpoint (`<slug>.progress.json`, ~first 100 candidates / ~15 min) covers front-of-book entities (races, classes). Spot-check the entities your change TARGETS there first — 790 green tests still shipped a Dragonborn→Monster regression that only the live early-check caught.
- [ ] **A count change is NOT automatically a regression — diff WHICH entities changed type.** Monster 34→30 this cycle was a CORRECTION (Acolyte/Noble/Sage/Soldier were mis-typed backgrounds), not a loss. Compare before/after type-maps per entity; never assume a drop is bad or a rise is good.
- [ ] **Read the real data shape before writing a predicate over it.** `EntityCandidate.TypePrior` always carries the scanner's frequency floor `{Monster,Spell,Item,Class}` — an "all priors gated" test could never fire. Check the producer (`HeadingCategoryClassifier.ExpandPrior`, the scanner) first.
- [ ] **When recall looks low, separate OUR-logic bugs from UPSTREAM/parser gaps.** If a missing entity is NOT in `declined.json`, it was never a candidate → upstream (Marker/scanner) gap, not our gate. (8 missing classes / 8 missing races this cycle were a parser gap → `mem:project_parser_upgrade_mineru`, not a false-drop.)
- [ ] **A reviewer "verified against the diff" ≠ behavioral truth.** Confirm against live code/producer and real output.

## Extraction pipeline facts

- Canonical source of truth: `books/canonical/<slug>.json`; siblings `<slug>.errors.json` / `.warnings.json` / `.declined.json` (declined = official-book gated non-matches). Checkpoints `<slug>.progress*.json` (deleted on success). Files are **root-owned** (container writes them) — edit via `docker cp` a host copy in. **Hand-authoring a single missing entity here is the designed escape hatch** — cheaper + safer than a 3rd parser/injector patch (`mem:project_extraction_recall_fixes`).
- Parser = **MinerU as a service** at `mineru:8000` (`MinerUPdfConverter` POSTs `/file_parse`, `-b pipeline -m ocr`), the SOLE `IPdfStructureConverter`; Marker is removed. Conversion disk-cache `books/conversion-cache/*.mineru.json` (`mem:project_parser_upgrade_mineru`).
- **CACHE GOTCHA (must-not-omit):** the conversion cache is keyed by **PDF hash only** — converter-LOGIC changes are NOT reflected. After ANY `MinerUPdfConverter` change, `docker exec … rm -f /books/conversion-cache/*.mineru.json` before re-extracting, or the OLD mapping is silently reused (cost a wasted full run this cycle).
- **Extraction is slow** (~30s/candidate, ~8.5 h/book) — qwen3 runs with thinking ON; `think:false` is a ~4-5× lever, and there is no page-range extract yet (full-book only), so early-checkpoint spot-checks are the fast-feedback substitute.
- Dev container lags `main` — **rebuild the app image** (`docker compose up -d --build app`) for code/config to take effect (`mem:project_dev_container_staleness`).

## Red Flags — STOP

- "Tests pass, so the extraction change works." → Run the live smoke, read the output.
- "The predicate looks right." → Read the real `TypePrior`/producer shape first.
- "Recall dropped, the gate is broken." → Check `declined.json` — absent ⇒ parser gap, not the gate.
- "I changed the converter; the smoke will test my fix." → A "cache hit" at run start means it tests the OLD mapping. Clear `*.mineru.json` first.
- "The count dropped — that's a regression." → Diff WHICH entities changed type; it may be a correction (mis-typed entities moving to the right type).
- "I'll patch the injector once more for this one entity." → 3rd patch on one entity = STOP. Hand-author it in the canonical.
- "It builds, so it's done." → Done = all gates green, output seen.
- "I'll edit the `.cs` directly." → Serena only.
- "I'll branch for this." → No. Single-dev, work on `main`.

## Common Mistakes

| Mistake | Fix |
|---------|-----|
| Coding before a design/proposal | Brainstorm → opsx:propose first. |
| Skipping the live validation on data changes | Smoke on PHB, inspect output + declined.json. |
| Predicate over an unread data shape | Read the producer (scanner/classifier) first. |
| Endpoint change without `.http`/insomnia update | Update both in the same commit. |
| Editing `.cs` by blind text replace | Serena `find_symbol`/`replace_symbol_body`. |
| Creating a feature branch | Work on `main` (single-dev). |
