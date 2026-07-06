# D&D Companion — Roadmap & Progress (living; refreshed 2026-07-06)

**North star:** a companion agent that REASONS (character build / encounter design / setting-aware
lore), not just retrieves. RAG + extraction are the *means*, and that foundation is now largely built —
the remaining north-star work is the REASONING layer (Items 3–4; Item 2 is DONE).

## Status legend: ✅ done · 🔄 in progress · ⬜ not started

## FOUNDATION — extraction + retrieval  ✅ (collapsed; see git history + archived changes)
Everything below shipped and is archived — do NOT re-plan it, just build on it:
- **WRITE-layer extraction quality:** recall fix + precision authoritative-allowlist + deterministic
  type resolution; generic 5etools backfill (`fivetools-entity-backfill`, archived 2026-07-04);
  **Object entity type + decline-not-leak** (archived 2026-07-05 — `mem:extraction/dmg_generic_backfill_status`).
- **Extraction perf — qwen3 /no_think:** SHIPPED (`803da7b`), ~8× faster, no classification regression.
- **Slice 1 character-fact-resolution:** shipped (`235699a`) — `CharacterResolutionService`,
  structured-fact store, `resolve_character_feature` MCP tool.

## FRONTIER — the REASONING layer (north star)
- **Item 2 — Slice 2: multiclass character** ✅ DONE (archived `multiclass-character`, 2026-07-05;
  18 commits `057e7e7..db16979`, 992/992 tests). GENERAL multiclass (any combo, caster or not — user was
  emphatic "not only spellcaster"): `CharacterSheet.Classes: List<ClassLevel>` source of truth + derived
  flat fields + tolerant legacy-JSON migration (set-only STJ sinks); `MulticlassRules` (prereqs +
  proficiency subsets, all 13 classes); `MulticlassSpellcasting` (combined caster level, Warlock pact
  carve-out); THREE seeded PHB slot tables (multiclass + half + third caster); resolution fork
  (single-class → own table; ≥2 spellcasting classes → combined) + per-class save DC / attack +
  `check_multiclass`; SEC-08 per-user MCP tools.
- **Item 3 — Auto-NeedsReview grounding cascade** ⬜ ← candidate NEXT (own brainstorm→spec). Tier 1 =
  embedding check (reuse mxbai `dnd_blocks` vectors → promote); Tier 2 = qwen3 judge on residual.
- **Item 4 — Corpus-wide dedup** ⬜ — dedup by canonical id / `EntityNameIndex` key, OUT of extraction.

## LOOSE ENDS / follow-ups
- **HeroDetail multiclass-editing UI** ✅ DONE (archived `hero-multiclass-editing`, 2026-07-05;
  commits a95c488,cdfec2c,4a70bb1,bae7c1b,c1e9d1a). Per-class list editor bound directly to
  `_editSheet.Classes` (add/remove class/level/subclass rows); `SetSingleClass` collapse REMOVED from
  `ConfirmSaveAsync` (footgun gone); `MulticlassRules.KnownClasses` dropdown; live derived total level +
  PB; non-blocking validity + reduced-proficiency advisory (non-primary rows); view mode lists all
  classes. Final opus review = MERGE-READY; fixed one spec-conformance Minor (prof advisory was on every
  row — plan had deviated from spec; SPEC governs). build 0/0, 978 non-persistence green.
  **Playwright UI smoke DONE (2026-07-06)** — via Playwright MCP against a local `dotnet run` on 5101
  (registered user `smoke_mc`, campaign+hero created live). VALIDATED: add class ×2 (live derived
  total 5→8, PB +3), remove class (2→1 rows, total→5), save 2-class hero, **hard reload persists**
  `Fighter 5 (Champion) / Wizard 3 (Evocation)` Level 8 + Session-1 snapshot; advisory scoped to
  non-primary rows only (row0=0, row1=2: "⚠ Requires Intelligence 13" validity + prof grant) —
  confirms c1e9d1a. Playwright gotcha: MCP browser context drops between tool calls (loses auth
  cookie + Blazor circuit) — drive login+multi-step edit in ONE `browser_run_code_unsafe` call.
  Deferred minors (still open): off-list class shows no selected `<option>` (display-only); unstyled
  CSS; bidirectional KnownClasses↔map-keys test.

  **✅ FIXED (commit `4d8b6db`, 2026-07-06): published-container Blazor UI static assets.** The Docker
  image's `/app/wwwroot` is EMPTY, so `app.UseStaticFiles()` (Extensions/AppExtensions.cs:24) 404s
  `app.css`, `app.js`, and `_framework/blazor.web.js` → no CSS + dead Blazor circuit in the container.
  Cause: .NET 9+ publish no longer copies static web assets into `wwwroot`; they're fingerprinted and
  served via `MapStaticAssets` + the `*.staticwebassets.endpoints.json` manifest (which IS in /app).
  `dotnet run` (dev) serves them fine. FIX APPLIED: `UseStaticFiles()` → `app.MapStaticAssets()`;
  moved `app.css`/`app.js` to project-root `wwwroot/`; dropped the `WebRootPath="CompanionUI/wwwroot"`
  override (Program.cs) + csproj `<WebRoot>`. Also hoisted `_Imports.razor` to `CompanionUI/` after the
  user moved `Layout/`+`Pages/` out of `Components/` (they'd lost their `@using`s). Reranker moved to the
  shared `shared_onnx_models` volume (`Reranker__ModelPath=models/ms-marco-MiniLM-L-6-v2` compose env,
  model seeded). `.playwright-mcp/` gitignored. Verified LOCALLY (`dotnet run`, SDK 10.0.203): /login,
  /app.css, /app.js, /_framework/blazor.web.js all 200; 978/978 non-persistence tests green.
  ⏳ CONTAINER REBUILD PENDING: couldn't rebuild the image to confirm — the env's nuget.org restore was
  failing ("Connection reset by peer"). Added a BuildKit NuGet cache mount to the Dockerfile to make
  restore resumable. Also NOTE the SDK feature-band question: earlier 301-SDK builds (during the flaky
  network) omitted `_framework/blazor.web.js` from the publish manifest while 203 emits it — likely an
  incomplete restore, RE-CONFIRM on a clean rebuild. `docker start dndmcpaicsharpfun-app-1` reuses the
  existing container (compose up trips on the external volume lookup).
- **Qdrant scalar int8 quantization:** shipped + archived; live-validated (recall preserved, ~4× vector
  memory). Effectively closed.
- **Spec housekeeping:** `extraction-think-mode` spec (config-toggle form) proposed, not applied
  (`/no_think` already shipped in `803da7b`).
- **DMG Object residuals** (hand-correctable): tighten `StatBlockScanner` naming / `IsObjectStatBlock`.

## How we progress (discipline — never skip)
Each item: **superpowers:brainstorming** (full dialogue) → **opsx:propose** (spec in
`openspec/changes/<name>/`) → **superpowers:writing-plans** → **superpowers:subagent-driven-development**
(per-task TDD + reviewer subagents; final whole-branch review on opus). Work DIRECTLY on main — no
feature branches (`mem:workflow/work_on_main`); commit autonomy granted. FINISH on "commit"/"archive":
commit → `openspec archive` → `skill-optimizer` → refresh this roadmap (`mem:workflow/finishing_a_spec`).
PLAN-VS-SPEC lesson (2026-07-05): a writing-plans plan can silently deviate from the approved spec it's
derived from (hero-multiclass-editing: plan said "prof advisory every row", spec said "non-primary
rows") — the final whole-branch review caught it; the SPEC governs. Cross-check the plan against the
spec's ADDED Requirements during writing-plans self-review.

## Current position (2026-07-06)
Extraction/retrieval FOUNDATION complete; **Item 2 (multiclass) + its HeroDetail UI follow-up both
SHIPPED + archived; Playwright UI smoke run + PASSED (2026-07-06).** Next: the frontier —
**Item 3 (grounding cascade)** or **Item 4 (dedup)**. Side finding to triage: published-container UI
static-assets bug (see loose-ends) — small standalone fix. Relates to
`mem:extraction/dmg_generic_backfill_status`, `mem:project_entity_extraction_rethink`,
`mem:reference_build_env_gotchas`.
