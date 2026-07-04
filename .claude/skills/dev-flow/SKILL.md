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
- **SECURITY REVIEW (HTTP / endpoint / config changes) — mandatory.** The `security-guidance` plugin runs pattern warnings on every edit and an async LLM review on `git commit`/`git push`. Do NOT dismiss it. For any HTTP endpoint (`MapGet/Post/Put/Delete`), request-body/query/header handling, admin-key/auth surface, MCP tool surface, or `Config/appsettings.*`/env/`docker-compose*` change, explicitly review the diff for: **authz/admin-gate gaps, injection/SSRF, secret exposure, and unsafe config/CORS/rate-limit**. If the commit/push hook re-wakes with findings, address or explicitly acknowledge each before continuing. Treat "no HTTP/config change in this diff" as the only reason to skip.

## Validation gates for DATA / EXTRACTION changes (must-not-omit — learned the hard way)

Unit-green is necessary, NOT sufficient for pipeline changes. Before claiming an extraction/scanner/resolver/gate change works:

- [ ] **Run a LIVE smoke on a real book** (`extract-entities` on PHB), then inspect the actual output — type counts, names, AND the reject/`declined.json` audit — before trusting or ingesting. (A green-tested gate was structurally DEAD in production this cycle; only the live run caught it.)
- [ ] **Clear the conversion cache after a converter change** (`docker exec … rm -f /books/conversion-cache/*.mineru.json`) — else the smoke runs the OLD mapping. A "cache hit" log at run start when you changed the converter means your fix is NOT being tested.
- [ ] **Early spot-check BEFORE the full ~8 h run.** The first progress checkpoint (`<slug>.progress.json`, ~first 100 candidates / ~15 min) covers front-of-book entities (races, classes). Spot-check the entities your change TARGETS there first — 790 green tests still shipped a Dragonborn→Monster regression that only the live early-check caught.
- [ ] **A count change is NOT automatically a regression — diff WHICH entities changed type.** Monster 34→30 this cycle was a CORRECTION (Acolyte/Noble/Sage/Soldier were mis-typed backgrounds), not a loss. Compare before/after type-maps per entity; never assume a drop is bad or a rise is good.
- [ ] **To pin a SILENT/untraced loss, build a CHEAP deterministic-path harness FIRST — don't burn a ~5-8 h live run to test a hypothesis.** Feed the real inputs (from the conversion cache) through the pipeline stages (matcher → resolver → drop-filter → dedup, and the real `TocCategoryMap` from bookmarks) in a unit test to locate the drop. This session it REFUTED two wrong page→category theories in minutes; the real cause was mis-tagged stat-line headings overwriting the section title. And whenever a valid item can vanish with no entity/decline/error/log, ADD a log at that silent path (the traceless drop cost a full session of diagnosis).
- [ ] **Read the real data shape before writing a predicate over it.** `EntityCandidate.TypePrior` always carries the scanner's frequency floor `{Monster,Spell,Item,Class}` — an "all priors gated" test could never fire. Check the producer (`HeadingCategoryClassifier.ExpandPrior`, the scanner) first.
- [ ] **When recall looks low, separate OUR-logic bugs from UPSTREAM/parser gaps.** If a missing entity is NOT in `declined.json`, it was never a candidate → upstream (Marker/scanner) gap, not our gate. (8 missing classes / 8 missing races this cycle were a parser gap → `mem:project_parser_upgrade_mineru`, not a false-drop.)
- [ ] **A reviewer "verified against the diff" ≠ behavioral truth.** Confirm against live code/producer and real output.
- [ ] **An in-place canonical rewrite (any tool/transform that edits `<slug>.json`) MUST end with a unique-id invariant + a load round-trip.** `CanonicalJsonLoader` THROWS on a duplicate id, so a rewrite that can map two entities onto the same id writes an un-loadable, un-ingestable file. Assert `ids.Should().OnlyHaveUniqueItems()` in a test AND reload the written file (or hit a real read/flag endpoint) before trusting it. This cycle a grounded-vs-grounded name collision wrote 6 duplicate ids that FOUR per-task-green reviews missed — only the whole-branch review + a real-data round-trip caught it. Reuse the extractor's own `EntityNameMatcher` + `EntityIdSlug` so the rewrite ≡ a re-extract (no second stripping/slug impl to drift), and prefer flag-`NeedsReview`-not-delete for ambiguous duplicates.

## Behavior-preserving refactor gates (dedup, moves, EF mapping, god-file splits — learned this cycle)

Structural changes that must not alter behavior have their OWN gates beyond "tests pass":

- [ ] **Before collapsing "duplicated" code, prove the copies are IDENTICAL.** If they diverge, PARAMETERIZE the difference or STOP and report — never silently pick one. This cycle: Monster `AlignMap` genuinely diverged (extra `NX`/`NY` keys) → kept local; `ExtractFeatureEntry` differed only by a `minParts` threshold → parameterized; and a "duplicate" `IsSidecar` check was missing `.declined.json` → collapsing to the canonical one FIXED a latent bug (declined files were mis-treated as canonical). Assume divergence until proven identical.
- [ ] **Schema-neutral EF change? PROVE it with an empty migration.** When moving mapping attributes ↔ Fluent config (or any refactor that must not touch the schema), run `dotnet ef migrations add VerifyNeutral`, confirm BOTH `Up()` and `Down()` are empty, then DELETE the migration + restore the model snapshot (`git checkout Migrations/AppDbContextModelSnapshot.cs`). A non-empty migration means your Fluent config diverged from the attributes — fix it, don't ship the drift.
- [ ] **Splitting a god file? A strong output-asserting test suite IS the "identical output before/after" guarantee.** Keep it 100% green and NEVER weaken an assertion to make the split pass; only ctor/DI wiring in the tests may change (every assertion stays byte-identical). If no such suite exists, add characterization tests FIRST. (STR-09 split a 657→381-line orchestrator behind its 17-test output suite with zero assertion changes.)
- [ ] **A tool's caller-identity comes from the trusted session, not a spoofable argument.** A tool acting on a user's data must CLOSE OVER the authenticated session's user id (server-verified ownership chain), never accept the id as a tool argument crossing a shared-key/loopback boundary where any key holder could assert it. Ship a NEGATIVE test (caller A cannot touch caller B's data → throws). (SEC-08: `resolve_character_feature` moved off the shared-key MCP surface into a per-user in-process tool.)
- [ ] **Explicit EF transactions + `EnableRetryOnFailure` = wrap in the execution strategy.** The production `AppDbContext` is registered with `EnableRetryOnFailure` (`ServiceCollectionExtensions.cs`), whose `NpgsqlRetryingExecutionStrategy` THROWS on a raw `db.Database.BeginTransactionAsync()` ("does not support user-initiated transactions"). Either drop the explicit transaction when a single `SaveChangesAsync` already gives atomicity, or wrap the multi-statement work in `db.Database.CreateExecutionStrategy().ExecuteAsync(async () => { … tx … })` (use a FRESH context inside the delegate if it Adds tracked rows, so retries don't double-insert). **The default test context has NO retry strategy, so 856 unit tests stayed green while ingestion failed on the first live run** — this is the canonical "unit-green ≠ prod-correct" trap. Guard it with a test built on a retry-ENABLED factory (`UseNpgsql(cs, o => o.EnableRetryOnFailure())`).

## Extraction pipeline facts

- Canonical source of truth: `books/canonical/<slug>.json`; siblings `<slug>.errors.json` / `.warnings.json` / `.declined.json` (declined = official-book gated non-matches). Checkpoints `<slug>.progress*.json` (deleted on success). Files are **root-owned** (container writes them) — edit via `docker cp` a host copy in. **Escape hatches for parser gaps, in preference order:** (1) for official-book SPELLS, `POST /admin/books/{id}/backfill-spells` — deterministic 5etools backfill, idempotent, gap-only, entities marked `dataSource:"5etools-backfill"` (this closed PHB 355→361); (2) hand-author the entity in the canonical (`mem:project_extraction_recall_fixes`). Both beat a 3rd parser/injector patch. **GOTCHA: `extract-entities?force=true` OVERWRITES the whole canonical → hand-authored AND backfilled entities are LOST. After every force re-extract, re-run `backfill-spells` and re-apply hand-authored entities** (e.g. re-add Gnome).
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
- "One more parser rule will reach the last N entities." → When parser iterations PLATEAU (PHB spells: +15, then +5, then the rest OCR-damaged beyond any clean rule), STOP grinding — switch to the authoritative backfill (official books) or hand-authoring. Measure the per-iteration yield; a falling curve is the signal.
- "It builds, so it's done." → Done = all gates green, output seen.
- "It's a pure dedup." → Prove the copies are byte-identical FIRST; divergence hides bugs (or a fix). Diverging → parameterize or stop.
- "Moving attributes to Fluent won't change the schema." → Prove it: an empty `dotnet ef migrations add` Up/Down, then discard the migration.
- "The god-file split passes tests." → Only if the suite asserts real OUTPUT and no assertion was weakened; wiring-only test edits.
- "The tool takes a userId argument." → Never trust a spoofable id across a shared-key boundary; close over the session identity + ship a negative-ownership test.
- "I'll edit the `.cs` directly." → Serena only.
- "I'll branch for this." → No. Single-dev, work on `main`.
- "It's a one-time data cleanup — I'll add an endpoint." → A one-shot migration is a `Tools/` console (like the retired `Tools/SqliteToPostgres`), NOT a permanent HTTP surface. The app's services are self-contained enough to reuse directly (`new EntityNameMatcher(new EntityNameIndex(fivetoolsDir))` needs no DI/DB). Endpoints are for behavior the product calls again.

## Common Mistakes

| Mistake | Fix |
|---------|-----|
| Coding before a design/proposal | Brainstorm → opsx:propose first. |
| Skipping the live validation on data changes | Smoke on PHB, inspect output + declined.json. |
| Predicate over an unread data shape | Read the producer (scanner/classifier) first. |
| Endpoint change without `.http`/insomnia update | Update both in the same commit. |
| Editing `.cs` by blind text replace | Serena `find_symbol`/`replace_symbol_body`. |
| Creating a feature branch | Work on `main` (single-dev). |
| Grinding parser rules past the yield plateau | Official-book spells: `backfill-spells`; otherwise hand-author. |
| Permanent endpoint for a one-time data migration | `Tools/` console reusing the app's services directly. |
| In-place canonical rewrite trusted on unit-green | Assert unique ids + reload the written file before ingesting. |
