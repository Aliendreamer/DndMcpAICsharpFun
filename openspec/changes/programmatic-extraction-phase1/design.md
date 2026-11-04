## Context

The app (single .NET 10 host: RAG API, MCP server, Blazor UI) serves structured D&D entities from
`books/canonical/<slug>.json`, ingested into the Qdrant collection `dnd_entities`. Today those files come from the qwen3:8b
extraction (`POST /admin/books/{id}/extract-entities`, reading MinerU output), patched by 5etools (declined gate,
field-fill, `5etools-backfill` records: 872 of 1,032 DMG magic items are copied wholesale). The owner wants data correct
from our own books, with no LLM and no 5etools, because a small model can't be trusted to produce data.

This change is Phase 1 of four. The approved program design (owner decisions D1–D9, my calls M1–M16) is the
brainstorming document `programmatic-extraction-design.md`. Its content relevant to Phase 1 is carried here.

### Spike evidence (2026-11-04, throwaway code)

CPU OCR of all three books took ~42 min with 3 parallel processes, then rule-based parsers. Scored against 5etools (the
answer key, used only to measure):

| Book | Measure | OCR + code | PDF text layer | Current model |
|---|---|---|---|---|
| MM (450) | found / AC / HP / CR / abilities / text | 419 / 100 / 100 / 100 / 93 / 99% | 254 / 97 / 98 / 98 / 91 / 95% | 315 / 80 / 69 / 46 / 15 / ~0% |
| PHB (361) | found / time / range / components / duration / text | 357 / 99 / 99 / 98 / 96 / 100% | 36 | mostly 5etools-copied |
| DMG (250 base items) | found / rarity / attunement / text | 243 / 98 / 99 / 100% | 218 / 98 / 99 / 97% | 126 / 78 / 86 / 100% |

The HP-vs-dice self-check flagged 6 of 425 monsters, and every one was a real OCR misread (Clay Golem "14d70").

## Goals / Non-Goals

**Goals:**

- A deterministic extractor service that produces Monster, Spell and MagicItem canonical entities for PHB/MM/DMG 2014,
  meeting or beating the spike's numbers on the dev scorer.
- Self-verification on every entity; automatic acceptance on a clean pass; flagged entities out of the chat until
  approved.
- A recognition report with a warning below 95%.
- An admin page to review flagged entities, browse all, override any field (books can be misprinted) and approve.
- Replace the old PHB/MM/DMG canonical data with the new output.

**Non-Goals:**

- Other entity types (Phase 2: character data; Phase 3: everything else).
- Prose chunks for `dnd_blocks`, and removing qwen3/5etools/MinerU (Phase 4).
- Any LLM in reading, parsing or verifying. Any GPU use (8 GB VRAM is shared with the owner's games).
- Chat, router and persona changes.

**Anti-goals:** a wrong value reaching the chat unflagged; guessing a name (the spike showed guessed names attach stats
to the wrong monster); re-runs wiping manual fixes; 5etools as a data source by the back door.

## Decisions

**Owner decisions (binding):**

- D1 deterministic, no model. D3 one book per run. D4 all checks pass → verified automatically; failures go to an
  admin list and are used only once approved; the owner handles only edge cases. D5 admin page with values next to the
  page image; override any value on any entity, verified ones included. D8 warn below 95% recognition. D9 no staging:
  delete the old JSONs and replace them with the new output; missing types until later phases are accepted.
- M1 note: the owner isn't convinced a separate container is needed but keeps it separate for now. So the HTTP boundary
  stays thin (submit, poll progress, fetch result, fetch page image) so it can be folded in later.

**Calls made in design (approved with the design):**

- **Extractor = Python service in its own container**, source in `Tools/BookExtractor/`, service `book-extractor` in
  this repo's compose (MinerU lives in the shared infra stack; ours ships with the app). Same package runs as a CLI for
  dev scoring. *Alternative:* a C# reader (PdfPig) was ruled out because the three books are scans with
  poor-to-unusable text layers, and the best CPU OCR stack (RapidOCR/ONNX) is Python.
- **Reader per page:** render 200 dpi (PyMuPDF), OCR with RapidOCR 3.x (`LangRec.EN`, `OCRVersion.PPOCRV5`), CPU. The
  text layer is used directly when the page's text quality passes a threshold measured on the born-digital PDFs in
  `books/` (SRD 5.1, Basic Rules). Reading order: two columns split at the midpoint, same-row fragments merged and
  sorted by x, headers and footers removed, hyphen joins, digit-noise repair (S→5, O→0, l/I→1, Z→2, B→8 only inside
  numeric or dice tokens). Each page's OCR result is cached on disk, so a crashed run resumes and parser re-runs don't
  re-OCR. OCR models are baked into the image at build time (no runtime download).
- **Parsers** from the spike, plus Phase 1 gap fixes: casting time wrapping a line (reaction spells), variant stat
  blocks (faerie dragon colours, bone naga forms, Lizard King/Queen), the ~7% ability-score misses, the DMG artifacts
  section (Eye/Hand of Vecna), multi-line item names, and the spike's missed entries (Spell Scroll, Figurine of
  Wondrous Power, Stone of Good Luck, Faerie Fire, Mass Cure Wounds, Mass Heal, Revivify).
- **Monster detection** anchors on a fuzzy "Armor Class" label; the size/alignment line sits 1–2 lines above; the name
  is the nearest larger-height line above it on the same page. No previous-page fallback. When no name is found the
  entity is flagged with an empty-name check failure, never given a guessed name.
- **Output contract:** the same canonical envelope and field shapes as today (`CanonicalJsonLoader`, `Schemas/canonical`),
  plus two envelope fields: `checks` (list of `{name, passed, reason}`) and `provenance` (`pages`, and per page a list of
  boxes in page-image pixel coordinates at 200 dpi). Schema version "1" → "2": the extractor writes "2". Other books'
  files stay "1" until their turn, so the loader accepts both, and "1" files simply have no checks/provenance.
- **Verified = existing `disposition`:** all checks pass → `Accepted`; any failure → `NeedsReview` and
  `needsReview: true`. Admin approval → `Accepted`. Only `Accepted` entities reach `dnd_entities` (existing behaviour).
  No bulk approve.
- **Ids:** `<book-slug>.<type>.<name-slug>`, the slug from the book registry / `EntityIdSlug` table. Variants get their own
  slug (`mm14.monster.faerie-dragon-red`). Ids are stable across our own runs; matching the old model-made ids isn't
  required (D9). Two entities with the same id in one run: the second gets a numeric suffix and a failed `unique-id`
  check.
- **`dataSource`** = `"programmatic"` and `authority` = `"book"` on every produced entity.
- **Overrides** in `books/canonical/<slug>.overrides.json`: list of `{entityId, field, value, reason, at}` where `field` is
  a JSON path inside the envelope (`fields.hitPoints.average`, `name`), plus approvals `{entityId, approved: true,
  reason, at}`. Applied by the app after the extractor returns, before writing the canonical file. Each override also
  stores the extracted value it replaced; if a later run extracts a different value there, the entity gets a failed
  `override-drift` check (so it lands in the review list) and the admin page says "extraction changed under your
  override". An override whose entity no longer exists is reported in the run result and kept.
- **Run flow:** `POST /admin/books/{id}/extract-programmatic` queues a run on a separate queue lane (so a 40-minute
  OCR run never blocks other ingestion). The app asks the extractor to process the PDF (read-only `books/` mount), polls
  progress, fetches the result, applies overrides, deletes the book's old canonical file and sidecars, writes the new
  file, and re-ingests the book into `dnd_entities` after deleting that book's old points. The run result (counts,
  recognition metrics, warning) is stored as `<slug>.report.json` next to the canonical file.
- **Recognition metrics:** (a) share of detected entities passing all checks; (b) share of rules pages whose text was
  assigned to an entity, or that the reader classified as prose/table for later phases. Pages without rules text
  (art-only, TOC, index, credits) are excluded by word count and layout. Warning if either is under 95%.
- **Admin access:** `User.IsAdmin` (bool, default false), one EF migration, set by the owner in pgAdmin. An authorization
  policy `Admin` guards the Blazor page `/admin/review`. Approve and override go through a C# service directly, not
  through the `X-Admin-Api-Key` HTTP surface.
- **Admin page:** filter by book and by flagged/all; entity view shows each field (editable) and the failed check
  reasons next to the page image with the provenance boxes highlighted. Page images come from the extractor
  (`GET /pages/{book}/{page}.png`), proxied by the app, cached on disk by the extractor.
- **Writes:** saving an override or approval rewrites the overrides file, re-applies it to the canonical file, and
  upserts (or removes) that one entity in `dnd_entities`. Single admin → no locking.
- **CPU cap:** the extractor container gets a CPU limit; default 2 processes × 4 threads, configurable.
- **Dev scorer:** `Tools/BookExtractor` CLI `score <book> <canonical.json> <5etools-dir>` (the spike's compare,
  including tag expansion and variant collapsing). Never imported by the service.

## Risks / Trade-offs

- [Classes, races, feats etc. disappear from the chat until Phase 2] → accepted by the owner (D9); the CLAUDE.md note
  and the run report say so.
- [OCR misreads that pass every check] → checks are layered (dice math, legal CR, XP matches CR, modifier matches score),
  and the dev scorer measures the residual. Unknown residual on books without an answer key.
- [Ids change between runs when a name OCR improves] → overrides keyed on an id that vanished are reported, not dropped.
- [40-minute runs] → per-page cache, resumable, own queue lane, CPU cap.
- [Root-owned `books/canonical` in the dev container] → fix ownership in the compose/Dockerfile (container user writes
  as the host user).
- [A separate container the owner may not want] → thin HTTP boundary keeps folding it in cheap.

## Migration Plan

1. Ship the extractor, the app integration, the schema bump and the admin page.
2. Run MM, PHB, DMG one at a time. Check the dev scorer meets the spike numbers; review the flagged list.
3. Each run deletes that book's old canonical file, sidecars and `dnd_entities` points, and writes the new data.
4. Rollback: the old files are in git history; restore them and re-ingest.

## Open Questions

None blocking. The text-layer quality threshold is measured during build on the born-digital PDFs.
