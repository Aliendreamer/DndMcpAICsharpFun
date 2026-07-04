# WHERE WE ARE — dmg-generic-backfill (2026-07-04, code DONE; DMG data run BLOCKED on GPU)

Change: `openspec/changes/dmg-generic-backfill/` (NOT archived). Generalized the two hand-rolled 5etools
backfill services into one provider-driven engine + 4 providers + type-parameterized endpoints, then added
two enhancements (rich Description flattening, +N variant expansion). Relates to `mem:extraction/mm_monster_status`,
`mem:companion_roadmap`.

## CODE — DONE + merge-ready on main (build 0/0, 921/921 non-persistence green)
Commits (in order):
- 8138b3b generic EntityBackfillService + IFivetoolsBackfillProvider (TDD, fake provider)
- 0cdd355 Monster+Spell providers (curated projection lifted verbatim from old services → parity)
- 01b9d40 MagicItem+God providers (new MagicItemFields/GodFields projections)
- 34a31a7 type-parameterized endpoints + DI dict + ported Monster/Spell test suites (assertions unchanged)
- 1c2d393 delete old MonsterBackfillService/SpellBackfillService + .http/.insomnia sync
- be1585c minor fixes (UnsupportedType IResult?→IResult; test doc)
- 07c2556 openspec: add flattening + variant tasks
- 7f336d2 FivetoolsEntryText.Flatten (recursive strings/entries/list/table + inline-tag strip); MagicItem+God Description rewired
- 39475aa MagicVariantExpander + roster wiring
- c9fced7 fix: array-valued requires/excludes predicates (ERLW variants were over-generating)
- ca1dd56 fix: scalar predicate vs array base-item field (Repeating Shot/Returning Weapon expanded to 0) — matcher now symmetric
- 1c40435 .http/.insomnia note (MagicItem = +N variants + rich descriptions)
All per-task reviewed + opus whole-branch review = MERGE-READY, no Critical/Important open. SDD ledger at
`.superpowers/sdd/progress.md` (section "dmg-generic-backfill").

## NEW routes (replace monster-recall/backfill-monsters/flag-unknown-monsters/backfill-spells)
- GET  /admin/books/{id}/entity-recall?type=Monster|Spell|MagicItem|God (report-only)
- POST /admin/books/{id}/backfill-entities?type=...
- POST /admin/books/{id}/flag-unknown-entities?type=...
- Unsupported ?type= → 400.

## Task 6 (DMG data run) — NOT DONE. Two blockers:
1. **App container is STALE** (`dndmcpaicsharpfun-app-1`, image built 2026-07-03) → still serves OLD routes
   (monster-recall→200, entity-recall→404). MUST rebuild the app image from current main to get new routes.
2. **GPU/Ollama DOWN** → fresh re-extraction (qwen3) + dnd_entities ingest blocked. Recall/backfill/flag/validate
   are deterministic (NO Ollama) and CAN run once the container is rebuilt.

DMG = **book id 3**, key DMG, status 6. Admin auth header `X-Admin-Api-Key`; value via
`docker exec dndmcpaicsharpfun-app-1 printenv Admin__ApiKey` (DON'T print/commit it). Ollama container =
`personalcommandcenter-ollama-1` (separate compose; app reaches it via `Ollama__BaseUrl=http://ollama:11434`).

## GPU ROOT CAUSE (investigated 2026-07-04) — Windows-host GPU-PV, NOT WSL/Docker
Env: single WSL distro Ubuntu-24.04, native Docker inside it (NO Docker Desktop). WSL 2.6.3, kernel 6.6.87.2,
DXCore 10.0.26100. `/dev/dxg` present, all /usr/lib/wsl/lib GPU libs present (driver 595.45.03), `.wslconfig`
does NOT disable GPU (`networkingMode=mirrored`, mem/proc/swap tuning only).
- `dmesg`: `misc dxg: dxgk: dxgkio_query_adapter_info: Ioctl failed: -22` at boot (6.3s) — host never handed
  this VM a GPU adapter.
- `nvmlInit_v2` → **9 = NVML_ERROR_DRIVER_NOT_LOADED**; `nvidia-smi` (WSL) → "Failed to initialize NVML: N/A".
- **WSL uptime was ~5h** at diagnosis → the VM ALREADY fresh-booted once (5h ago) and the -22 STILL fired →
  a WSL restart does NOT fix it. It's the Windows→WSL GPU handoff.
FIX (Windows side, user must do — restart imminent): (1) `wsl --update` (refreshes dxgkrnl/GPU-PV — highest
odds); (2) clean-reinstall the Windows NVIDIA driver; (3) reboot Windows (clears wedged GPU-PV). Bisect test:
`nvidia-smi` in ELEVATED Windows PowerShell — works → do #1; fails → do #2. Success gate: `nvidia-smi` works
INSIDE WSL (and `cat /proc/uptime` low after a real shutdown).

## AFTER RESTART — resume order
1. Verify `nvidia-smi` works in WSL.
2. `docker start personalcommandcenter-ollama-1`; confirm qwen3 loads on GPU (`ollama list`).
3. Rebuild app image from main (compose project dndmcpaicsharpfun, service app) → new routes live.
4. Full DMG flow: POST extract-entities?force=true (id 3) → review dmg14.json diff (14 bogus Class gone) →
   entity-recall the 4 types → backfill-entities + flag-unknown-entities each → canonical/validate (0 fail for
   dmg14) → commit corrected dmg14.json → (deferred) ingest-entities.
   NOTE: user pre-approved "Rebuild + run deterministic pipeline" — if GPU still down, can rebuild + run
   recall/backfill/flag/validate against CURRENT dmg14 (no Ollama) to live-validate the new code, deferring the
   fresh extract.
5. FINISH: fix stale route names in dev-flow SKILL.md + `mem:operations/running_the_stack` (old routes) →
   `openspec archive dmg-generic-backfill -y` → run skill-optimizer.

## Deferred/known minors (final-review triage)
- Inline-tag regex misses argument-less `{@i}` / semantic `{@atk mw}` (rare in item/god text).
- "5etools-backfill" literal inlined in Spell/MagicItem/God providers (Monster uses BackfillDataSource const).
- MagicItem Description flattens top-level + tables/lists/nested; magicvariant expansion covers common
  placeholders; both intentionally pragmatic v1.
