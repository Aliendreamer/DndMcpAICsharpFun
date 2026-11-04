# Programmatic Extraction Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A deterministic Python book extractor (OCR + rules, no LLM) that produces verified Monster, Spell and
MagicItem canonical entities for PHB/MM/DMG 2014, wired into the app with an overrides file, an `IsAdmin` guard and a
Blazor review page, replacing the old model-made data for those three books.

**Architecture:** `Tools/BookExtractor/` is a Python package (`bookextractor`) with a reader (render → OCR or text layer
→ reading order → noise repair), three parsers, checks, an assembler that emits entities + a recognition report, a
FastAPI service and a CLI (extract / score). The app talks to it over HTTP from its own background queue, applies
`<slug>.overrides.json`, sets disposition from checks, replaces the book's canonical file and sidecars, and re-ingests.
A Blazor page `/review` lets an `IsAdmin` user override and approve.

**Tech Stack:** Python 3.12, rapidocr 3.4.2 (ONNX Runtime, CPU), PyMuPDF, rapidfuzz, FastAPI + uvicorn, pytest,
jsonschema. .NET 10, EF Core (Npgsql), Blazor Server, xunit + FluentAssertions, Testcontainers Postgres.

**Spec:** `openspec/changes/programmatic-extraction-phase1/` — `proposal.md`, `design.md`,
`specs/programmatic-book-extraction/spec.md`, `specs/entity-verification-review/spec.md`.

**Spike reference (throwaway, port from it, don't import it):**
`/tmp/claude-1000/-home-aliendreamer-projects-DndMcpAICsharpFun/6ab16f2f-2ad0-495d-ad20-e45f0bab2658/scratchpad/pdfspike/`
— `extract.py` (reader + parsers), `compare.py` (scorer), `py_ocr_book.py` (OCR loop), `SPIKE.md` (results), and the
OCR cache `ocr/{mm,phb,dmg}/NNN.json` (1-based page, `{w,h,lines:[{box:[[x,y]×4],t,s}]}`), venv at `../pyenv`.

## Global Constraints

- No LLM call and no GPU anywhere in the extractor. CPU only: `onnxruntime` (not `-gpu`).
- Render at **200 dpi**; RapidOCR params `{"Rec.lang_type": LangRec.EN, "Rec.ocr_version": OCRVersion.PPOCRV5,
  "EngineConfig.onnxruntime.intra_op_num_threads": N}`; default 2 processes × 4 threads, configurable.
- OCR page cache format is exactly the spike's: `<cache>/<pdf-sha256>/ocr/NNN.json` (3-digit, 1-based) with
  `{w,h,lines:[{box,t,s}]}`, so the spike cache can seed it.
- Canonical schema version written: `"2"`; loader accepts `"1"` and `"2"`. `dataSource` = `"programmatic"`,
  `authority` = `"book"`.
- Ids `<book-slug>.<type-lower>.<name-slug>` (type-lower: `monster`, `spell`, `magicitem` — match existing ids, e.g.
  `mm14.monster.goblin`); slug from `EntityIdSlug` (existing C#). Duplicate id → `-2`, `-3` suffix + failed `unique-id`.
- Field shapes MUST validate against `Schemas/canonical/{Monster,Spell,MagicItem}Fields.schema.json`.
- 95% warning threshold for both recognition shares.
- 5etools is read only by the `score` CLI; never by the service or the app's programmatic path.
- Repo rules: Serena for all `.cs` edits; Central Package Management (versions in `Directory.Packages.props`);
  warnings-as-errors; `.http` + `dnd-mcp-api.insomnia.json` updated with any endpoint change; markdown lint clean
  (`pnpm lint:md`); work and commit on `main`; `dotnet` commands need `dangerouslyDisableSandbox` (git-crypt);
  never read `Config/appsettings*.json` or `.env*`; do not stage `.serena/project.yml` or Config noise.
- The Blazor review page lives at `/review` — NOT under `/admin` (the API-key middleware gates `/admin/*`).

## Review Focus

1. A stat block whose name line is missing or on the previous page → entity gets an empty name and a failed
   `name-found` check, never a neighbour's name (test in Task 3).
2. A page that OCRs to zero lines (art page, blank) → no crash, page counted as non-rules (test in Task 2).
3. An override whose field path doesn't exist in the entity (typo, schema change) → rejected on save with a clear
   message, not a silent no-op (test in Task 10).
4. Extractor unreachable or a run that times out → the run fails cleanly and the old canonical file and points stay
   (test in Task 11).
5. A non-admin user, or an admin whose flag was removed after sign-in → `/review` denied on the next request (handler
   reads the DB each time; test in Task 12).

---

### Task 1: Package skeleton, page model, reading order and digit repair

**Files:**
- Create: `Tools/BookExtractor/pyproject.toml`, `Tools/BookExtractor/README.md`,
  `Tools/BookExtractor/bookextractor/__init__.py`, `bookextractor/model.py`, `bookextractor/layout.py`
- Create: `Tools/BookExtractor/tests/fixtures/ocr/<book>/NNN.json` (golden pages copied from the spike cache)
- Test: `Tools/BookExtractor/tests/test_layout.py`
- Modify: `.gitignore` (add `Tools/BookExtractor/.venv/`, `**/__pycache__/`)

**Interfaces:**
- Produces (`model.py`):
  - `@dataclass Fragment(text: str, score: float, box: tuple[float,float,float,float])` (x0,y0,x1,y1 in 200-dpi px)
  - `@dataclass Line(text: str, box: tuple[float,float,float,float], height: float, page: int, frags: list[Fragment])`
  - `@dataclass Page(number: int, width: int, height: int, lines: list[Line], source: Literal["ocr","layer"])`
  - `load_ocr_page(path: Path, number: int) -> Page` (raw lines, not ordered)
- Produces (`layout.py`): `reading_order(page: Page) -> list[Line]`, `fix_digits(text: str) -> str`,
  `join_hyphens(lines: list[Line]) -> list[Line]`.

Golden pages: copy from the spike cache the pages containing — MM: Goblin, Clay Golem, Faerie Dragon, Lizard King/Queen,
Demilich; PHB: Counterspell, Fireball, Faerie Fire, Revivify; DMG: Amulet of Proof against Detection and Location,
Eye of Vecna, Spell Scroll, Figurine of Wondrous Power. Find them with `grep -l` on the cache. Record page numbers in
`tests/fixtures/PAGES.md`.

- [ ] **Step 1: Write failing tests** — `test_fix_digits_dice` (`"45 (6dlO + 12)"` → `"45 (6d10 + 12)"`),
  `test_fix_digits_leaves_words` (`"Slashing Bludgeoning"` unchanged), `test_fix_digits_cr` (`"Challenge l/4"` →
  `"Challenge 1/4"`), `test_reading_order_columns` (on the Goblin page: the line containing `Armor Class` comes after the
  `GOBLIN` name line and before `Hit Points`; no line mixes left- and right-column text), `test_ability_row_order` (Goblin
  ability row reads `8 (-1) 14 (+2) 10 (+0) 10 (+0) 8 (-1) 8 (-1)` in that order), `test_headers_footers_removed` (no
  ordered line equals the running header or a bare page number).
- [ ] **Step 2:** `cd Tools/BookExtractor && python3 -m venv .venv && .venv/bin/pip install -e ".[dev]" && .venv/bin/pytest -q` → FAIL (import errors).
- [ ] **Step 3: Implement** by porting `reading_order`, `fix_digits`, `join_text` from the spike. Keep the spike's
  fragment list sorted by x0 when merging same-row fragments (the fix that took ability scores 23% → 91%).
  Digit repair maps S→5, O→0, l/I→1, Z→2, B→8 only in tokens matching a numeric/dice pattern.
  `pyproject.toml` pins: `rapidocr==3.4.2`, `onnxruntime` (CPU), `pymupdf`, `rapidfuzz`, `numpy`, `fastapi`,
  `uvicorn`; dev extra: `pytest`, `jsonschema`, `httpx`. Use the versions in the spike venv.
- [ ] **Step 4:** `.venv/bin/pytest -q` → all PASS.
- [ ] **Step 5: Commit** `feat(book-extractor): package skeleton, reading order, digit repair`.

### Task 2: Page reader (OCR, text layer, cache, rules-page classifier)

**Files:**
- Create: `bookextractor/reader.py`, `bookextractor/pages.py`
- Test: `tests/test_reader.py`, `tests/test_pages.py`

**Interfaces:**
- Consumes: `Page`, `load_ocr_page` (Task 1).
- Produces:
  - `class BookReader(pdf_path: Path, cache_dir: Path, threads: int = 4, processes: int = 2)` with
    `read(progress: Callable[[int,int],None] | None = None) -> list[Page]` and `render_png(page: int) -> bytes`
    (200 dpi, cached at `<cache>/<sha>/png/NNN.png`).
  - `pdf_sha256(path: Path) -> str`
  - `layer_quality(page: pymupdf.Page) -> float` (0–1) and `LAYER_QUALITY_THRESHOLD: float`
  - `classify_page(page: Page) -> Literal["rules","art","toc","index","credits","blank"]`

- [ ] **Step 1: Failing tests** — `test_cache_hit_skips_ocr` (pre-populate cache dir with a fixture page; monkeypatch
  the OCR engine to raise; `read()` returns that page), `test_resume_after_interrupt` (pages 1–2 cached, OCR stub called
  only for page 3 of a 3-page generated PDF), `test_blank_page_no_crash` (OCR stub returns `boxes=None` → Page with
  0 lines, `classify_page` = `"blank"`), `test_layer_used_when_quality_passes` (a born-digital page generated with
  PyMuPDF `insert_text` → `source == "layer"`), `test_classify_index_page` (fixture MM index page → `"index"`).
- [ ] **Step 2:** run → FAIL.
- [ ] **Step 3: Implement.** OCR loop = spike `py_ocr_book.py`, parallel over pages with a `ProcessPoolExecutor`
  (`processes` workers, each its own RapidOCR engine with `threads`). Write each page's JSON atomically (temp + rename).
  Measure `LAYER_QUALITY_THRESHOLD` on `books/` born-digital PDFs (SRD 5.1, Basic Rules — find by title via PyMuPDF
  metadata) versus the three scans; record the measured values in a comment. Quality = share of layer words that are
  dictionary-like (letters only, length ≥ 2, vowel present) — the scans' layers score low.
- [ ] **Step 4:** run → PASS.
- [ ] **Step 5: Commit** `feat(book-extractor): page reader with OCR cache, text layer, page classifier`.

### Task 3: Monster parser and checks

**Files:**
- Create: `bookextractor/parsers/__init__.py`, `bookextractor/parsers/monster.py`, `bookextractor/checks.py`
- Test: `tests/test_monster.py`

**Interfaces:**
- Consumes: `reading_order`, `Line`, `Page`.
- Produces:
  - `@dataclass Check(name: str, passed: bool, reason: str)` in `checks.py`
  - `@dataclass Parsed(type: str, name: str, fields: dict, text: str, lines: list[Line], checks: list[Check],
    variant_of: str | None = None)` in `parsers/__init__.py`
  - `parse_monsters(lines: list[Line]) -> list[Parsed]` (`type="Monster"`)
  - `monster_checks(p: Parsed) -> list[Check]` — names: `hp-matches-dice`, `abilities-present`, `ac-present`,
    `hp-present`, `cr-present`, `speed-present`, `cr-legal`, `xp-matches-cr`, `ability-modifiers`.

`fields` uses the MonsterFields shapes (see a current `books/canonical/mm14.json` record, e.g. Goblin:
`size:["S"]`, `type:{type:"humanoid",tags:["goblinoid"]}`, `alignment:["N","E"]`, `ac:[{ac:15,from:[...]}]`,
`hp:{average:7,formula:"2d6"}`, `speed:{walk:30}`, `str..cha` ints, `cr:"1/4"`, `trait`/`action`/`reaction`/
`legendary` as `[{name, entries:[str]}]`).

- [ ] **Step 1: Failing tests** — `test_goblin` (name `Goblin`, ac 15, hp 7 / `2d6`, str 8 dex 14, cr `1/4`, all checks
  pass), `test_clay_golem_hp_mismatch` (fixture reads `14d70`; `hp-matches-dice` fails and its reason contains `133`
  and `14d70`), `test_name_never_guessed` (remove the name line from a fixture page's lines → name `""` and failed
  `name-found` — check added by Task 6, so here assert name `""`), `test_faerie_dragon_variants` (one Parsed per colour,
  `variant_of="Faerie Dragon"`, names like `Faerie Dragon (Red)`), `test_lizard_king_queen` (parsed as one entity with
  the printed name), `test_hp_formula_math` (parametrized: `(7,"2d6")` pass, `(133,"14d10+56")` pass, `(52,"8d8+16")`
  fail).
- [ ] **Step 2:** run → FAIL.
- [ ] **Step 3: Implement** by porting `monster_starts`, `pick_name`, `parse_monsters`, `hp_check` from the spike, then
  fix the gaps: variant blocks, ability rows split across two lines (the ~7% misses — accept the six score/modifier
  pairs from the two lines after the `STR DEX CON INT WIS CHA` header), Lizard King/Queen. XP per CR from the standard
  table (CR 0 = 0 or 10, 1/8 = 25, 1/4 = 50 … 30 = 155,000).
- [ ] **Step 4:** run → PASS.
- [ ] **Step 5: Commit** `feat(book-extractor): monster parser and checks`.

### Task 4: Spell parser and checks

**Files:**
- Create: `bookextractor/parsers/spell.py`
- Test: `tests/test_spell.py`

**Interfaces:**
- Produces: `parse_spells(lines: list[Line]) -> list[Parsed]` (`type="Spell"`), `spell_checks(p) -> list[Check]` —
  names: `level-school`, `casting-time`, `range`, `components`, `duration`, `concentration-agrees`.
- Field shapes per SpellFields (`level`, `school` one-letter code as in the current `phb14.json`, `time:[{number,unit,
  condition?}]`, `range:{type, distance:{type, amount?}}`, `components:{v,s,m?}`, `duration:[{type, duration?,
  concentration?}]`, `ritual`, `concentration`, `entries`, `entriesHigherLevel`). Copy shapes from existing records.

- [ ] **Step 1: Failing tests** — `test_fireball` (level 3, school `V`, time `[{number:1,unit:"action"}]`, range 150
  feet point, components v/s/m with `"a tiny ball of bat guano and sulfur"`, duration instant, all checks pass,
  `entriesHigherLevel` non-empty), `test_counterspell_wrapped_time` (time unit `reaction` with a `condition` containing
  `see a creature within 60 feet`; `casting-time` passes), `test_faerie_fire_found`, `test_revivify_found`,
  `test_concentration_agrees` (a duration `Concentration, up to 1 minute` → `concentration: true`).
- [ ] **Step 2:** run → FAIL.
- [ ] **Step 3: Implement** by porting `parse_spells` and fixing: casting time continuing onto the next line until
  `Range:`; entries whose level line OCRs with noise (Faerie Fire, Mass Cure Wounds, Mass Heal, Revivify were missed —
  find why on their fixture pages and fix the start detection).
- [ ] **Step 4:** run → PASS.
- [ ] **Step 5: Commit** `feat(book-extractor): spell parser and checks`.

### Task 5: Magic item parser and checks

**Files:**
- Create: `bookextractor/parsers/item.py`
- Test: `tests/test_item.py`

**Interfaces:**
- Produces: `parse_items(lines: list[Line]) -> list[Parsed]` (`type="MagicItem"`), `item_checks(p) -> list[Check]` —
  names: `type-parsed`, `rarity-parsed`.
- Fields per MagicItemFields: `type`, `rarity` (lowercase as in current `dmg14.json`, e.g. `"uncommon"`,
  `"artifact"`, `"varies"`), `reqAttune` (`true` or the condition string), `wondrous`, `entries`.

- [ ] **Step 1: Failing tests** — `test_two_line_name` (`Amulet of Proof against Detection and Location`, uncommon,
  reqAttune true), `test_artifact_section` (`Eye of Vecna` rarity `artifact`, reqAttune true), `test_spell_scroll_found`,
  `test_figurine_found`, `test_no_doubled_names` (no parsed name contains the same 2-word sequence twice).
- [ ] **Step 2:** run → FAIL.
- [ ] **Step 3: Implement** by porting `parse_items` + `key_eq` and adding the artifacts section (its type line is
  `Wondrous item, artifact (requires attunement)` and the headings are larger) and the missed entries.
- [ ] **Step 4:** run → PASS.
- [ ] **Step 5: Commit** `feat(book-extractor): magic item parser and checks`.

### Task 6: Assembler — ids, common checks, provenance, output, report, CLI extract

**Files:**
- Create: `bookextractor/assemble.py`, `bookextractor/report.py`, `bookextractor/cli.py`
- Test: `tests/test_assemble.py`, `tests/test_output_schema.py`

**Interfaces:**
- Consumes: Tasks 2–5.
- Produces:
  - `slugify(name: str) -> str` (lowercase, `[^a-z0-9]+` → `-`, trimmed; variant `Faerie Dragon (Red)` →
    `faerie-dragon-red`)
  - `extract_book(pages: list[Page], book_slug: str, types: set[str]) -> ExtractionResult`
  - `@dataclass ExtractionResult(entities: list[dict], report: dict)`
  - Each entity dict: `{id, type, name, page, canonicalText, fields, checks:[{name,passed,reason}],
    provenance:{pages:[{page, boxes:[{x0,y0,x1,y1}]}]}}` (camelCase; the app adds book-level fields).
  - Report dict: `{book, pagesTotal, pagesRules, pagesCovered, counts:{Monster,Spell,MagicItem}, passingShare,
    coverageShare, warnings:[str], failed:[{id, checks:[name]}]}`; shares are 0–1 floats; warning text
    `"recognition {x:.1%} is under 95%: <which share>"`.
  - CLI: `python -m bookextractor extract <pdf> --slug <slug> --cache <dir> --out <file.json> [--threads N --processes N]`
    writes `{entities, report}`.
- Common checks added to every entity: `name-found`, `text-present`, `numeric-clean` (no letters inside dice or numeric
  fields), `unique-id`.

- [ ] **Step 1: Failing tests** — `test_ids` (`mm14.monster.goblin`, `phb14.spell.fireball`,
  `dmg14.magicitem.amulet-of-proof-against-detection-and-location`), `test_duplicate_id_suffixed` (two entities named
  `Wolf` → ids `…wolf`, `…wolf-2`; the second has failed `unique-id`), `test_provenance_two_pages` (a spell crossing pages
  lists both pages with ≥1 box each), `test_report_warning` (9 of 10 entities passing → `passingShare` 0.9 and one
  warning containing `90.0%`), `test_non_rules_pages_excluded` (index/blank pages not in `pagesRules`),
  `test_fields_validate_schema` (every fixture entity's `fields` validates with `jsonschema` against
  `Schemas/canonical/<Type>Fields.schema.json` from the repo root).
- [ ] **Step 2:** run → FAIL.
- [ ] **Step 3: Implement.** Coverage: a rules page counts as covered when any of its lines belongs to an entity, or it
  holds ≥ N prose lines (prose for later phases; N = 10). `canonicalText` = the entity's joined text.
- [ ] **Step 4:** run → PASS.
- [ ] **Step 5: Commit** `feat(book-extractor): assembler, report and extract CLI`.

### Task 7: Dev scorer CLI

**Files:**
- Create: `bookextractor/score.py`
- Test: `tests/test_score.py`

**Interfaces:**
- Produces: `python -m bookextractor score <mm|phb|dmg> <extract-output.json> --fivetools <dir>` printing per-field
  agreement and text coverage/precision (the spike's table), exit 0. `score_book(book, entities, fivetools_dir) -> dict`.

- [ ] **Step 1: Failing tests** — `test_tag_expansion` (`{@atk mw}` → `Melee Weapon Attack:`, `{@hit 4}` → `+4`,
  `{@h}` → `Hit: `, `{@dc 13}` → `DC 13`), `test_variant_collapse` (5etools `Potion of Healing (Greater)` collapses to
  `Potion of Healing`), `test_service_never_imports_score` (importing `bookextractor.service` does not import
  `bookextractor.score`; check `sys.modules`).
- [ ] **Step 2:** run → FAIL.
- [ ] **Step 3: Implement** by porting `compare.py` (drop the "model" column; ours only).
- [ ] **Step 4:** run → PASS.
- [ ] **Step 5: Commit** `feat(book-extractor): dev scorer against 5etools`.

### Task 8: HTTP service, Dockerfile, compose

**Files:**
- Create: `bookextractor/service.py`, `Tools/BookExtractor/Dockerfile`, `Tools/BookExtractor/.dockerignore`
- Modify: `docker-compose.yml`, `docker-compose.prod.yml`
- Test: `tests/test_service.py`

**Interfaces:**
- Produces (HTTP, port 8000):
  - `POST /jobs` body `{pdfPath: str (relative to BOOKS_DIR), bookSlug: str, types?: [str]}` → `202 {jobId}`;
    `409` if a job is running (one job at a time); `400` if the path escapes `BOOKS_DIR` or the file is missing.
  - `GET /jobs/{jobId}` → `{status: "queued"|"running"|"done"|"failed", pagesDone, pagesTotal, error?}`
  - `GET /jobs/{jobId}/result` → the `{entities, report}` JSON (404 until done)
  - `GET /page-image?pdfPath=…&page=N` → `image/png`
  - `GET /health` → `200`
- Env: `BOOKS_DIR=/books`, `CACHE_DIR=/cache`, `OCR_THREADS=4`, `OCR_PROCESSES=2`.

- [ ] **Step 1: Failing tests** (FastAPI `TestClient`, reader monkeypatched to fixture pages) — `test_job_lifecycle`,
  `test_path_traversal_rejected` (`../etc/passwd` → 400), `test_second_job_conflict` (409), `test_page_image_png`
  (content-type `image/png`).
- [ ] **Step 2:** run → FAIL.
- [ ] **Step 3: Implement.** Job runs in a background thread; state in memory (a lost job after restart is fine: the OCR
  cache makes a re-submit cheap). Dockerfile: `python:3.12-slim`, install the package, then run a warm-up that
  constructs `RapidOCR(...)` once at build time so models are baked into the image; non-root user. Compose service
  `book-extractor`: build `Tools/BookExtractor`, volumes `./books:/books:ro` and named volume
  `book_extractor_cache:/cache`, `deploy.resources.limits.cpus: "8"`, network `shared`, healthcheck on `/health`.
  App service gets `BookExtractor__ServiceUrl=http://book-extractor:8000`. Prod compose: same service, `books_data:/books:ro`.
- [ ] **Step 4:** run tests → PASS; `docker compose build book-extractor` succeeds (needs network for pip/model
  download: allow PyPI and `www.modelscope.cn`).
- [ ] **Step 5: Commit** `feat(book-extractor): HTTP service and container`.

### Task 9: Canonical envelope v2 and programmatic ingestion rules (C#)

**Files:**
- Create: `Domain/Entities/EntityCheck.cs`, `Domain/Entities/EntityProvenance.cs`
- Modify: `Domain/Entities/EntityEnvelope.cs`, `Domain/Entities/CanonicalJsonFile.cs`,
  `Features/Entities/CanonicalJsonLoader.cs`, `Features/Ingestion/Entities/EntityIngestionOrchestrator.cs`
- Test: `DndMcpAICsharpFun.Tests/Entities/CanonicalJsonLoaderV2Tests.cs`,
  `DndMcpAICsharpFun.Tests/Entities/ProgrammaticIngestionTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record EntityCheck(string Name, bool Passed, string Reason);`
  - `public sealed record PageBox(int X0, int Y0, int X1, int Y1);`
    `public sealed record ProvenancePage(int Page, IReadOnlyList<PageBox> Boxes);`
    `public sealed record EntityProvenance(IReadOnlyList<ProvenancePage> Pages);`
  - `EntityEnvelope` gains trailing optional params `IReadOnlyList<EntityCheck> Checks = null!` (init `?? []`) and
    `EntityProvenance? Provenance = null`.
  - `CanonicalJsonSchema.CurrentVersion = "2"`, `CanonicalJsonSchema.SupportedVersions = ["1", "2"]`.
  - `public static class DataSources { public const string Programmatic = "programmatic"; }` (in `EntityEnvelope.cs`).
- Ingestion rule: for envelopes with `DataSource == DataSources.Programmatic`, skip unless
  `Disposition == Accepted`, and skip the 5etools enrichment and the existing-Qdrant merge (use the envelope as is).
  `ReindexEntityAsync` deletes the point instead of upserting for a non-Accepted programmatic entity. Other books keep
  today's behaviour.

- [ ] **Step 1: Failing tests** — `Loads_v1_and_v2`, `Rejects_v3`, `Roundtrips_checks_and_provenance`,
  `Programmatic_NeedsReview_not_ingested`, `Programmatic_Accepted_ingested_without_fivetools_merge` (fake 5etools index
  with a conflicting value; ingested envelope keeps the programmatic value), `NonProgrammatic_NeedsReview_still_ingested`
  (today's behaviour unchanged).
- [ ] **Step 2:** `dotnet test --filter "FullyQualifiedName~CanonicalJsonLoaderV2|FullyQualifiedName~ProgrammaticIngestion"` → FAIL.
- [ ] **Step 3: Implement** (Serena edits). Regenerate schemas by building (delete `obj/canonical-schemas.stamp` first).
- [ ] **Step 4:** the filtered tests PASS; `dotnet build` 0 warnings.
- [ ] **Step 5: Commit** `feat(entities): canonical v2 envelope with checks and provenance`.

### Task 10: Overrides file, merge and disposition (C#)

**Files:**
- Create: `Features/Ingestion/Programmatic/EntityOverrides.cs`, `Features/Ingestion/Programmatic/OverrideMerger.cs`,
  `Features/Ingestion/Programmatic/OverridesStore.cs`
- Test: `DndMcpAICsharpFun.Tests/Programmatic/OverrideMergerTests.cs`, `…/OverridesStoreTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record FieldOverride(string EntityId, string Field, JsonElement Value, JsonElement? Extracted,
    string Reason, DateTimeOffset At);`
  - `public sealed record Approval(string EntityId, string Reason, DateTimeOffset At);`
  - `public sealed record EntityOverrides(IReadOnlyList<FieldOverride> Overrides, IReadOnlyList<Approval> Approvals);`
    (`EntityOverrides.Empty`)
  - `OverridesStore(IOptions<EntityIngestionOptions>)`: `Task<EntityOverrides> LoadAsync(string bookSlug, CancellationToken)`
    (missing file → Empty), `Task SaveAsync(string bookSlug, EntityOverrides, CancellationToken)` (atomic write,
    indented camelCase) — file `books/canonical/<slug>.overrides.json`.
  - `OverrideMerger.Apply(IReadOnlyList<EntityEnvelope> extracted, EntityOverrides o) -> MergeResult`
    `public sealed record MergeResult(IReadOnlyList<EntityEnvelope> Entities, IReadOnlyList<string> OrphanedEntityIds);`
  - `OverrideMerger.SetDisposition(EntityEnvelope e, bool approved) -> EntityEnvelope`: all checks pass or approved →
    `Accepted`, `NeedsReview=false`; else `NeedsReview` + `NeedsReview=true`.
  - `OverrideMerger.ValidatePath(EntityEnvelope e, string field) -> bool`
- Field path: dot-separated camelCase, root `name`, `canonicalText`, or `fields.<…>` (array index as a number segment,
  e.g. `fields.ac.0.ac`). Drift: when `Extracted` differs from the newly extracted value at that path, append
  `EntityCheck("override-drift", false, "extraction changed under your override: was <a>, now <b>")`. The override
  value is still applied. Drift always flags, even if the entity was approved before. The admin's next approval (or a
  re-save of the override) sets that override's `Extracted` to the new value, which clears the drift.

- [ ] **Step 1: Failing tests** — `Override_applied` (Grell `fields.cr` → `"3"`), `Override_survives_rerun`,
  `Drift_flags_entity`, `Orphan_reported_and_kept`, `Approval_accepts_failed_entity`, `Clean_entity_accepted`,
  `Invalid_path_rejected` (`fields.nope.x` → `ValidatePath` false), `Store_missing_file_is_empty`,
  `Store_roundtrip`.
- [ ] **Step 2:** run filtered → FAIL.
- [ ] **Step 3: Implement** with `System.Text.Json.Nodes` for path get/set.
- [ ] **Step 4:** PASS.
- [ ] **Step 5: Commit** `feat(programmatic): overrides file, merge and disposition`.

### Task 11: Extractor client, queue, run flow and endpoint (C#)

**Files:**
- Create: `Features/Ingestion/Programmatic/BookExtractorOptions.cs`, `BookExtractorClient.cs`,
  `ProgrammaticExtractionRunner.cs`, `ProgrammaticExtractionQueue.cs` (BackgroundService + `IProgrammaticExtractionQueue`)
- Modify: `Features/Admin/BooksAdminEndpoints.cs`, DI registration (where `IIngestionQueue` is registered),
  `Features/Admin/CanonicalSidecarFiles.cs` (reuse for sidecar names), `DndMcpAICsharpFun.http`,
  `dnd-mcp-api.insomnia.json`, `CLAUDE.md`
- Test: `DndMcpAICsharpFun.Tests/Programmatic/ProgrammaticExtractionRunnerTests.cs`, `…/BookExtractorClientTests.cs`,
  `…/ExtractProgrammaticEndpointTests.cs`

**Interfaces:**
- Consumes: Task 9 types, Task 10 `OverridesStore`, `OverrideMerger`; existing `IIngestionTracker.GetByIdAsync`,
  `EntityIdSlug.BookSlug(record)`, `IEntityIngestionOrchestrator.IngestEntitiesAsync`.
- Produces:
  - `BookExtractorOptions { string ServiceUrl = "http://localhost:8000"; TimeSpan PollInterval = 10s; TimeSpan Timeout = 4h; }`
    bound from section `BookExtractor`.
  - `IBookExtractorClient`: `Task<string> SubmitAsync(string pdfPath, string bookSlug, CancellationToken)`,
    `Task<ExtractorJobStatus> GetStatusAsync(string jobId, CancellationToken)`,
    `Task<ExtractorResult> GetResultAsync(string jobId, CancellationToken)`,
    `Task<byte[]> GetPageImageAsync(string pdfPath, int page, CancellationToken)`.
  - `ExtractorResult(IReadOnlyList<ExtractedEntity> Entities, JsonElement Report)`;
    `ExtractedEntity(string Id, EntityType Type, string Name, int? Page, string CanonicalText, JsonElement Fields,
    IReadOnlyList<EntityCheck> Checks, EntityProvenance Provenance)`.
  - `IProgrammaticExtractionRunner.RunAsync(int bookId, CancellationToken) -> Task<ProgrammaticRunResult>`;
    `ProgrammaticRunResult(int Entities, int Accepted, int NeedsReview, IReadOnlyList<string> Orphans, JsonElement Report)`.
  - `IProgrammaticExtractionQueue.TryEnqueue(int bookId) -> bool` (own channel and in-flight guard; separate from
    `IngestionQueueWorker`).
  - Endpoint `POST /admin/books/{id}/extract-programmatic` → `202 Accepted` with `{bookId}`; `404` unknown book;
    `409` already queued/running.
- Run flow: submit (pdfPath = record's file path relative to `Ingestion:BooksPath`) → poll until done/failed/timeout →
  build envelopes (sourceBook/edition/firstAppearedIn from the record and its 5etools-free metadata, `DataSource`
  programmatic, `Authority` "book") → `OverrideMerger.Apply` → `SetDisposition` → **only now** delete
  `<slug>.json` and every sidecar (`.errors`, `.warnings`, `.declined`, `.progress.json`, `.progress.errors.json`), write
  new `<slug>.json` (schema "2") and `<slug>.report.json` → `IngestEntitiesAsync(bookId)` (its stale-delete removes old
  points). Any failure before the delete leaves files and points untouched.

- [ ] **Step 1: Failing tests** — `Success_replaces_files_and_sidecars` (temp canonical dir with `mm14.json`,
  `mm14.errors.json`, `mm14.declined.json`; fake client returns 2 entities, one failing a check → new `mm14.json` v2 with
  1 Accepted + 1 NeedsReview, sidecars gone, `mm14.report.json` exists, ingestion called once),
  `Failure_keeps_old_files` (client throws `HttpRequestException` → files byte-identical, ingestion not called),
  `Timeout_keeps_old_files`, `Overrides_applied_before_write`, `Endpoint_202_404_409`, `Client_parses_result` (stub
  `HttpMessageHandler`).
- [ ] **Step 2:** run filtered → FAIL.
- [ ] **Step 3: Implement.** Register the `HttpClient` like `MinerUPdfConverter`. Update `.http` and Insomnia with the
  new route (admin key header), and CLAUDE.md "Adding a new book" + endpoints list (the programmatic route, note that
  PHB/MM/DMG use it). `pnpm lint:md` → 0 errors.
- [ ] **Step 4:** PASS; `dotnet build` 0 warnings.
- [ ] **Step 5: Commit** `feat(programmatic): extractor client, run flow and endpoint`.

### Task 12: IsAdmin and the Admin policy (C#)

**Files:**
- Modify: `Domain/User.cs`, `Infrastructure/Persistence/AppDbContext.cs`, `Features/Auth/UserRepository.cs`,
  `Extensions/AuthExtensions.cs`
- Create: `Migrations/<timestamp>_AddUserIsAdmin.cs` (via `dotnet ef migrations add AddUserIsAdmin`),
  `Features/Auth/AdminAuthorization.cs`
- Test: `DndMcpAICsharpFun.Tests/Persistence/UserIsAdminTests.cs`, `DndMcpAICsharpFun.Tests/Auth/AdminAuthorizationTests.cs`

**Interfaces:**
- Produces: `User(long Id, string Username, string PasswordHash, bool IsAdmin = false)`;
  `UserRepository.IsAdminAsync(long id) -> Task<bool>`; `public static class AuthPolicies { public const string Admin = "Admin"; }`;
  `AdminRequirement : IAuthorizationRequirement`; `AdminAuthorizationHandler` reads `ClaimTypes.NameIdentifier` and calls
  `IsAdminAsync` on every evaluation (no claim caching).

- [ ] **Step 1: Failing tests** — `IsAdmin_defaults_false` (Testcontainers), `IsAdminAsync_reads_flag`,
  `Handler_succeeds_for_admin`, `Handler_fails_for_non_admin`, `Handler_fails_after_flag_removed` (flip in DB between
  two evaluations), `Handler_fails_without_name_identifier`.
- [ ] **Step 2:** run filtered → FAIL.
- [ ] **Step 3: Implement**; migration column `IsAdmin boolean NOT NULL DEFAULT false`.
- [ ] **Step 4:** PASS (Docker must be running for the persistence tests).
- [ ] **Step 5: Commit** `feat(auth): IsAdmin flag and Admin policy`.

### Task 13: Review service and Blazor `/review` page (C#)

**Files:**
- Create: `Features/Admin/Review/EntityReviewService.cs`, `CompanionUI/Pages/Review/Review.razor` (+ `.razor.css` if
  the design system uses scoped CSS), nav link in `CompanionUI/Layout/MainLayout.razor` shown only to admins
- Test: `DndMcpAICsharpFun.Tests/Admin/EntityReviewServiceTests.cs`, a bUnit test only if bUnit is already used in the
  test project (otherwise service tests + the live check in Task 14)

**Interfaces:**
- Consumes: `OverridesStore`, `OverrideMerger`, `CanonicalJsonLoader`, `IEntityIngestionOrchestrator.ReindexEntityAsync`,
  `IBookExtractorClient.GetPageImageAsync`, `AuthPolicies.Admin`.
- Produces:
  - `ReviewListItem(string Id, string Name, EntityType Type, string BookSlug, int? Page, bool Flagged, IReadOnlyList<string> FailedReasons)`
  - `EntityReviewService`: `ListAsync(string? bookSlug, bool flaggedOnly, CancellationToken) -> Task<IReadOnlyList<ReviewListItem>>`
    (programmatic books only: canonical files with schema "2"), `GetAsync(string entityId, CancellationToken) -> Task<EntityEnvelope?>`,
    `OverrideAsync(string entityId, string field, JsonElement value, string reason, CancellationToken)` (throws
    `ArgumentException` on an invalid path or empty reason), `ApproveAsync(string entityId, string reason, CancellationToken)`,
    `GetPageImageDataUriAsync(string entityId, int page, CancellationToken) -> Task<string>`.
  - Each write: load overrides → add → save → re-apply to the canonical file (rewrite it) → `ReindexEntityAsync`.
- Page: `@page "/review"`, `@attribute [Authorize(Policy = AuthPolicies.Admin)]`. Left: list with book filter and
  flagged/all toggle. Right: entity fields as editable JSON per top-level field (name, canonicalText, each `fields.*`),
  failed check reasons, reason box, Save override / Approve buttons (Approve only when flagged); below or beside it the
  page image with provenance boxes drawn as absolutely positioned outlines scaled to the displayed image width.
  Follow the existing visual design system in `wwwroot/app.css`.

- [ ] **Step 1: Failing tests** — `List_flagged_only`, `Approve_makes_entity_accepted_and_reindexes`,
  `Override_on_accepted_entity_updates_file_and_point`, `Override_invalid_path_throws`, `Override_empty_reason_throws`.
- [ ] **Step 2:** run filtered → FAIL.
- [ ] **Step 3: Implement** service, then the page.
- [ ] **Step 4:** PASS; `dotnet build` 0 warnings; full `dotnet test` green.
- [ ] **Step 5: Commit** `feat(review): entity review service and /review page`.

### Task 14: Live runs and verification

**Files:**
- Create: `openspec/changes/programmatic-extraction-phase1/report.md`
- Replaced by the runs: `books/canonical/{mm14,phb14,dmg14}.json` (+ `.report.json`); deleted: their `.errors`,
  `.warnings`, `.declined`, `.progress*` sidecars.

- [ ] **Step 1:** Full `dotnet build` + `dotnet test` green; `Tools/BookExtractor/.venv/bin/pytest -q` green.
- [ ] **Step 2:** Rebuild and start the stack (`docker compose up -d --build app book-extractor`). Seed the extractor cache
  from the spike OCR cache: copy `ocr/<tag>/*.json` into the `book_extractor_cache` volume at `<pdf-sha256>/ocr/` for
  each book (sha of the matching PDF in `books/`), so no 40-minute OCR re-run is needed. Fix `books/canonical`
  ownership if the app can't write.
- [ ] **Step 3:** For MM, then PHB, then DMG (one at a time): `POST /admin/books/{id}/extract-programmatic`; wait for
  completion; run `python -m bookextractor score <book> …` against the new canonical file and `5etools/`. Must meet or
  beat the spike: MM found ≥ 419, AC/HP/CR ≥ 99%, abilities ≥ 93%; PHB found ≥ 357, every spell field ≥ 96%; DMG found
  ≥ 243, rarity ≥ 98%, attunement ≥ 99%. Fix and re-run if not (parser re-runs are fast with the cache).
- [ ] **Step 4:** Live check as an admin user (set `IsAdmin` in pgAdmin): open `/review`, pick a flagged monster, confirm
  `GET /retrieval/entities/{id}` returns 404; approve it; confirm it now returns the entity. Override one field on an
  Accepted entity and confirm the retrieval shows the new value.
- [ ] **Step 5:** Write `report.md` (per-book scorer tables, flagged counts, warnings, what remains for Phase 2), lint,
  commit the new canonical files, reports and `report.md`: `feat(data): programmatic PHB/MM/DMG canonical data`.
