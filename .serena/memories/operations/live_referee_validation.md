# Live-validating the web authority referee (T3) — technique + findings (2026-07-17)

`extraction-authority-ladder` T3 (web authority referee) shipped + was validated **end-to-end live**. The referee only fires on the **keyless no-match residual** (`isOfficial = !string.IsNullOrWhiteSpace(record.FivetoolsSourceKey)` in `EntityExtractionOrchestrator`; keyless → `isOfficial=false` → no-5etools-match candidates route to `IWebAuthorityReferee`).

## How to exercise it live (reusable)
1. Rebuild app with a `docker-compose.override.yml` injecting `Admin__ApiKey=${ADMIN_API_KEY}` (env overrides the git-crypt appsettings key — the real key is masked in-sandbox) + `WebAuthorityReferee__Enabled=true`. `ADMIN_API_KEY=$(...) docker compose up -d --build app`.
2. A book only hits the referee if **keyless AND has bookmarks**. EEPC (id 9) is keyless but has **no PDF bookmarks** → both block-ingest and the bookmark-derived candidate scanner fail. Workaround that worked: **register an already-bookmarked official PDF (MTF, MPMM) as a keyless copy** (distinct displayName → distinct canonical slug, omit `fivetoolsSourceKey`). Same file hash → MinerU conversion cache hits (fast). `extract-entities` reads the conversion directly — **no `ingest-blocks` needed**.
3. Monitor via the per-100 checkpoint files `books/canonical/<slug>.progress.json` (authority field is serialized into canonical JSON). Terminal status = `EntitiesExtracted`.
4. Cleanup: `DELETE /admin/books/{id}` (also removes the canonical JSON + PDF copy), rm leftover `.declined`/`.errors`, delete the override, `docker compose up -d --force-recreate app` to revert to referee-off + git-crypt key (verify injected key now 401).

## Results (proof)
- MTF keyless: 137 entities → 136 `canon`, 1 `homebrew`.
- MPMM keyless: 303 entities → 290 `canon`, **3 `verified-thirdparty`** (Deep Gnome Traits, Archdruid, Rot Grub), 10 `homebrew`.
- ALL THREE referee verdicts observed live (keyless no-match → real SearXNG call → confirm/miss → label; nothing dropped). Referee CONFIRMED working end-to-end.

## Findings (two, distinct)
1. **Referee under-confirms real canon** (Babau, Tlincalli, Ulitharid, Tortle → `homebrew`): name-variant mismatch (plural "Tortles" vs index "Tortle") + **SearXNG upstream-engine throttling** (we host the aggregator; its backends — wikidata/DDG — return `403 suspended` under rapid load, so fewer results → refute-bias → homebrew). Recoverable (never dropped). Tunable: query name-normalization/singularization + retry-on-throttle. `AuthoritativeDomains` already retuned from a live probe (5e.tools/5esrd.com/fandom appear; dndbeyond/roll20 bot-blocked).
2. **Extraction-noise leak (SEPARATE bug, not T3)**: stat-block fragments / section headings ("Damage Immunities poison", "AN ANARCH's LAIR", "Effects of the Mold", raw AC/HP lines) mis-extracted as Monster entities in keyless books — should have been declined. Referee correctly refuses to confirm them (→ homebrew) but they reveal an upstream `IsRealEntity`/candidate-scan gap in the keyless path. → tracked as a NEW openspec change (2026-07-17).

Related: `mem:operations/running_the_stack`, `mem:companion_roadmap`.
