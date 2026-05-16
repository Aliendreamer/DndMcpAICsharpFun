# 5etools Direct Ingestion + ClassFields Alignment + Re-ingest Strategy

## Decisions Log

Decisions are recorded here as they are made. This is the live source of truth for the design session.

---

## Topic 1: 5etools Direct Ingestion Path

### Endpoint

- `POST /admin/5etools/import`
- **No body, no parameters** — triggers ingest of everything in the `5etools/` directory
- Imports all sources, all entity types in one shot
- Creates a stub `Book` record per source if one does not exist yet

### Source Registry

- Static dictionary in code mapping 5etools source acronyms → list of file paths
- Known sources: PHB, MM, DMG, TCE, XGE, SCAG (extendable)
- Each entry records whether the source is `core` or `supplement` (no need to pass this at runtime)
- Example entry: `"PHB" → ["class/class-phb.json", "spells/spells-phb.json", "backgrounds/backgrounds-phb.json", ...]`

### Entity Type Coverage

- Ingest ALL entity types found in the registry for a source
- Auto-discover: if a file is in the registry entry, it is processed; unmappable types are skipped with a warning

### Conflict Resolution (Provenance)

- `EntityEnvelope` gets a `Source` field: `"5etools"` | `"llm"` | `"manual"`
- Before upserting into `dnd_entities`, the orchestrator checks existing entity `_source`:
  - `"manual"` → **skip** (hand corrections are sacred)
  - `"llm"` or `"5etools"` or absent → **overwrite**
- 5etools data always wins over LLM-extracted data

### Pipeline

- Direct ingest into `dnd_entities` — **no** canonical JSON file written to disk
- Approach: extend existing `EntityIngestionOrchestrator` (add provenance check + source field)
- New `FivetoolsToEnvelopeMapper` layer: 5etools JSON → `EntityEnvelope[]`

### canonicalText Generation

- Per-type renderer for every entity type ingested from 5etools
- No fallback to field concatenation — every type gets a proper renderer
- Renderer style: structured and terse, RAG-optimised (matching Spell/Monster renderer style)

---

## Topic 2: ClassFields C# Record Alignment

### Scope

- `ClassFields.cs` — update record to 5etools shape: `Hd` (object with `number`+`faces`), `Proficiency` (string array), `StartingProficiencies` (object), `ClassFeatures` (array), `Multiclassing` (object), `Entries` (array)
- `SubclassFields.cs` — update to 5etools shape: replace old `ClassLevelEntry`/`FeatureRef` references with 5etools array-of-arrays `SubclassFeatures`
- `ClassCanonicalTextRenderer.cs` — re-create (was deleted after dispatcher was cleared)
- `EntityCanonicalTextDispatcher` — re-wire Class + Subclass cases

### canonicalText Style for Class

- Option C: structured and terse, RAG-optimised (same style as Spell/Monster renderers)
- Pipe-source tags stripped from feature names (e.g. `"Fighting Style|PHB"` → `"Fighting Style"`)

---

## Topic 3: Re-ingest Strategy

### Stale LLM-Extracted Files

- No new tooling needed
- User deletes stale progress/canonical files manually
- Re-runs `POST /admin/books/{id}/extract-entities` with the corrected prompts
- Provenance tracking (Topic 1) ensures 5etools-ingested entities cannot be overwritten by LLM re-extraction

### Protection Model

- Any entity ingested via `POST /admin/5etools/import` gets `_source: "5etools"`
- Subsequent LLM extraction for the same book will upsert with `_source: "llm"` — orchestrator sees existing `_source: "5etools"` and **skips**
- Manual corrections set `_source: "manual"` — neither 5etools nor LLM can overwrite

---

## Still Being Decided

- (nothing open)
