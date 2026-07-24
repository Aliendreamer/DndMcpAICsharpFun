# Read-path / Bin-B frontier status (2026-07-18, CORRECTED)

The read-path router (from archived prose-grounded-knowledge-model §H/§I) is built out:
- **chat-query-router** (Slice 1, archived) — pre-LLM classifier narrows the tool set to the query's tool GROUP (hybrid signals + mxbai embedding backstop), safe full-set fallback. `Features/Chat/Routing/`.
- **entity-set-query** (Slice 2, archived) — `list_entities` complete deterministic filter-SET via Qdrant Count+Scroll over indexed dnd_entities fields. Compact rows, honest total-vs-returned. `GET /retrieval/entities/list`.
- **spell-class-join** (archived) — `castableByClass` filter; `SpellClassIndex` loads `5etools/spells/sources.json` (801/920, all 10 caster classes), query-time join. No migration/GPU.

## CORRECTION (2026-07-18): items 2 & 3 do NOT need prose extraction for official content
Earlier I wrote that race aggregation (item 2) and tables (item 3) require multi-day GPU-bound prose extraction. **That was WRONG.** 5etools already has the structured data for all our official/5etools-covered books — same deterministic-lookup path as spell-class-join:
- **Race attributes** (`5etools/races.json`): Dragonborn `ability=[{"str":2,"cha":1}]`, `resist=[{"choose":{"from":["acid","cold","fire","lightning","poison"]}}]`. **74/157 races** have a structured ability bonus, **42/157** a structured resistance/immunity.
- **Tables** (structured `{"type":"table"}` entries in the race/class entries): the Draconic Ancestry table is present verbatim — `colLabels ['Dragon','Damage Type','Breath Weapon']`, rows `["Black","Acid","5 by 30 ft. line (Dex. save)"] ...`. The exact table the prose-grounded design tried to extract from OCR-noisy prose is clean JSON in 5etools.

**So the correct framing:**
- **Item 2 (race aggregation)** = project 5etools `ability`/`resist` → structured entity fields (like fivetools-field-fill / spell-class-join) → deterministic filter. GPU-free, in-session. **ABILITY-BONUS HALF SHIPPED 2026-07-24 (`race-ability-filter`, suite 1704/1704):** `abilityBonus=str` on `GET /retrieval/entities/list` returns races boosting that ability (fixed OR choosable), query-time in-memory match mirroring `castableByClass` (NO re-index) — `RaceAbilityParser` reads the boosted set from the entity's raw `Fields` JsonElement (RaceFields.Ability was already field-filled). Real-Qdrant grounding test proved Fields round-trip + parse. STILL OPEN: the **resistance/immunity** filter (same pattern over `RaceFields.Resist`/`Immune`) — deferred.
- **Item 3 (tables)** = project 5etools `{"type":"table"}` entries → `CanonicalTable` (StructuredFactProjector already CONSUMES CanonicalTable into Postgres StructuredTables/Rows; CharacterResolutionService.ResolveBreathWeaponAsync already resolves against them). So pulling the Dragonborn table FROM 5etools lights up the existing resolution engine — no prose extraction, no GPU.
- **Prose extraction (the hard, GPU-bound, uncertain path) is only needed for HOMEBREW / non-5etools books** — a smaller, separable, later concern, NOT the official corpus.

The recurring pattern: **for official content, 5etools is the deterministic structured source; prefer projecting from it over LLM prose extraction.** Same lesson that made spell-class-join trivial. Related: [[project_entity_extraction_rethink]], [[companion_roadmap]], the shipped `fivetools-field-fill`/`fivetools-entity-backfill`.
