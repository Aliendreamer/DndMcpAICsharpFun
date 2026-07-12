# Fivetools Field-Fill Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A field-level 5etools gap-fill that patches only missing allowlisted structured fields onto extraction's canonical entities (never overwriting extraction/prose), auto-run after extraction, plus an admin endpoint and a one-time cleanup of a wholesale import.

**Architecture:** A pure `EntityFieldMerger` (fill-missing-only merge over an entity's `Fields` JSON, tracked by a reserved `_fivetoolsFilledFields` key), driven by an `EntityFieldFillService` that, per book, indexes the 5etools roster by name (via `FivetoolsSourceRegistry` + `FivetoolsMapperRegistry`) and merges each canonical entity of a type that has a structured-field allowlist. Reuses `CanonicalJsonLoader`/`CanonicalJsonWriter` (atomic, deterministic).

**Tech Stack:** .NET 10, System.Text.Json (`JsonElement`/`JsonObject`/`JsonNode`), the shipped `Features/Ingestion/FivetoolsIngestion/` + `Features/Entities/` machinery, xUnit + FluentAssertions.

## Global Constraints

- **Serena for ALL `.cs` reads/edits** — built-in Read/Edit/Write on code files is forbidden. `.http`/`.insomnia`/`.md` may use plain tools.
- **Warnings-as-errors** — every build 0 warnings / 0 errors.
- `dotnet` commands run with `dangerouslyDisableSandbox: true` (git-crypt Config); the full suite needs Docker (Testcontainers). Generous timeouts.
- **Extraction is source of truth.** The fill patches ONLY allowlisted *structured* fields; it NEVER touches `entries`/prose, NEVER overwrites a field extraction produced, and skips `dataSource == "manual"` entities entirely.
- **Provenance** lives in a reserved `_fivetoolsFilledFields` string-array *inside* the entity's `Fields` — no `EntityEnvelope`/canonical-schema change. Entity `dataSource` stays extraction.
- **Idempotent:** running the fill twice yields a byte-identical canonical (rely on `CanonicalJsonWriter.WriteAsync` = atomic temp+rename with deterministic `CanonicalJson.WriteOptions`).
- **New HTTP route** (`fill-fields`) → update `DndMcpAICsharpFun.http` AND `dnd-mcp-api.insomnia.json` in the same commit.
- **A new `Add*`/DI registration** goes in Program AND the `FullContainerScopeValidationTests` replica if the scope test covers it; final verify runs the **full** `dotnet test`.

---

## File Structure

- **Create** `Features/Ingestion/FivetoolsIngestion/FieldFillAllowlist.cs` — static per-type `EntityType → IReadOnlySet<string>` structured-field allowlist.
- **Create** `Features/Ingestion/FivetoolsIngestion/EntityFieldMerger.cs` — pure fill-missing-only merge over one entity's `Fields`.
- **Create** `Features/Ingestion/FivetoolsIngestion/EntityFieldFillService.cs` — per-book orchestration (index 5etools roster, merge each entity, write canonical) → a report.
- **Modify** `Features/Admin/BooksAdminEndpoints.cs` — add `POST /admin/books/{id}/fill-fields`.
- **Modify** the extraction completion path (Task 4 — the site is confirmed with Serena) to auto-run the fill.
- **Modify** `Extensions/ServiceCollectionExtensions.cs` (or the relevant `Add*`) — register `EntityFieldFillService`.
- **Modify** `DndMcpAICsharpFun.http`, `dnd-mcp-api.insomnia.json`.
- **Test** `DndMcpAICsharpFun.Tests/Entities/Admin/EntityFieldMergerTests.cs`, `EntityFieldFillServiceTests.cs`.

---

### Task 1: Allowlist config + pure field merger

**Files:**
- Create: `Features/Ingestion/FivetoolsIngestion/FieldFillAllowlist.cs`
- Create: `Features/Ingestion/FivetoolsIngestion/EntityFieldMerger.cs`
- Test: `DndMcpAICsharpFun.Tests/Entities/Admin/EntityFieldMergerTests.cs`

**Interfaces:**
- Consumes: `System.Text.Json` (`JsonElement`, `JsonObject`, `JsonNode`); `EntityType` (`Domain.Entities`).
- Produces:
  - `FieldFillAllowlist.For(EntityType) → IReadOnlySet<string>?` (null when the type has no allowlist).
  - `EntityFieldMerger.Merge(JsonElement entityFields, IReadOnlySet<string> allowlist, JsonElement fivetoolsFields) → (JsonElement Fields, bool Changed)` — fill-missing-only; tracks the reserved `_fivetoolsFilledFields` key.

**Context:** The merge is the heart of the feature. Rules per allowlisted field `f` that the 5etools record has: **absent** in the entity → copy from 5etools, add `f` to `_fivetoolsFilledFields`; **present and in `_fivetoolsFilledFields`** → overwrite with the 5etools value (deterministic re-derive); **present and not listed** → leave untouched (extraction/human). `_fivetoolsFilledFields` is written last and sorted, so a second run is byte-identical. Never consider `entries` (it isn't in any allowlist).

- [ ] **Step 1: Write the failing tests**

```csharp
// DndMcpAICsharpFun.Tests/Entities/Admin/EntityFieldMergerTests.cs
using System.Text.Json;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Admin;

public class EntityFieldMergerTests
{
    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();
    private static readonly IReadOnlySet<string> ClassAllow =
        new HashSet<string> { "hd", "classFeatures", "subclassTitle" };

    [Fact]
    public void FillsMissingAllowlistedFields_recordsProvenance_leavesEntriesAlone()
    {
        var entity = J("""{ "entries": ["prose"] }""");
        var five = J("""{ "hd": {"faces":12}, "classFeatures": ["Rage"], "subclassTitle": "Path", "entries": ["5e prose"], "hasFluff": true }""");

        var (merged, changed) = EntityFieldMerger.Merge(entity, ClassAllow, five);

        changed.Should().BeTrue();
        merged.GetProperty("hd").GetProperty("faces").GetInt32().Should().Be(12);
        merged.GetProperty("classFeatures").GetArrayLength().Should().Be(1);
        merged.GetProperty("entries")[0].GetString().Should().Be("prose");   // extraction prose untouched
        merged.TryGetProperty("hasFluff", out _).Should().BeFalse();          // non-allowlisted 5etools field NOT pulled
        merged.GetProperty("_fivetoolsFilledFields").EnumerateArray()
            .Select(x => x.GetString()).Should().BeEquivalentTo(["hd", "classFeatures", "subclassTitle"]);
    }

    [Fact]
    public void NeverOverwritesAnExtractionProducedField()
    {
        var entity = J("""{ "hd": {"faces":10} }""");           // extraction already has hd, NOT provenance-marked
        var five = J("""{ "hd": {"faces":12} }""");
        var (merged, changed) = EntityFieldMerger.Merge(entity, ClassAllow, five);
        merged.GetProperty("hd").GetProperty("faces").GetInt32().Should().Be(10);   // extraction wins
    }

    [Fact]
    public void ReRun_isByteIdentical_idempotent()
    {
        var entity = J("""{ "entries": ["p"] }""");
        var five = J("""{ "hd": {"faces":8}, "classFeatures": ["X"], "subclassTitle": "T" }""");
        var (first, _) = EntityFieldMerger.Merge(entity, ClassAllow, five);
        var (second, changed2) = EntityFieldMerger.Merge(first, ClassAllow, five);
        second.GetRawText().Should().Be(first.GetRawText());
        changed2.Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run to verify they fail** — `dotnet test --filter "FullyQualifiedName~EntityFieldMergerTests"` → FAIL (types missing).

- [ ] **Step 3: Implement `FieldFillAllowlist`**

```csharp
// Features/Ingestion/FivetoolsIngestion/FieldFillAllowlist.cs
using System.Collections.Frozen;
using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

/// <summary>Per-type STRUCTURED-field allowlist for the 5etools field-fill. Structured mechanics only —
/// never <c>entries</c>/prose (extraction owns content). Covering a type = adding an entry here.</summary>
public static class FieldFillAllowlist
{
    private static readonly FrozenDictionary<EntityType, IReadOnlySet<string>> Map =
        new Dictionary<EntityType, IReadOnlySet<string>>
        {
            [EntityType.Class] = Set("hd", "classFeatures", "subclassTitle", "proficiency", "spellcastingAbility", "casterProgression", "classTableGroups", "startingProficiencies", "multiclassing"),
            [EntityType.Subclass] = Set("subclassFeatures", "subclassTableGroups", "spellcastingAbility", "casterProgression"),
            [EntityType.Spell] = Set("level", "school", "range", "components", "duration", "time", "classes"),
            [EntityType.Monster] = Set("environment", "traitTags", "senseTags", "languageTags"),
        }.ToFrozenDictionary();

    public static IReadOnlySet<string>? For(EntityType type) => Map.TryGetValue(type, out var s) ? s : null;

    private static IReadOnlySet<string> Set(params string[] fields) => fields.ToFrozenSet();
}
```

- [ ] **Step 4: Implement `EntityFieldMerger`**

```csharp
// Features/Ingestion/FivetoolsIngestion/EntityFieldMerger.cs
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

/// <summary>Fill-missing-only merge of allowlisted 5etools fields onto one entity's Fields. Never
/// overwrites an extraction/human field; re-derives previously-5etools-filled fields (deterministic);
/// records provenance in a reserved <c>_fivetoolsFilledFields</c> array. Idempotent.</summary>
public static class EntityFieldMerger
{
    private const string ProvenanceKey = "_fivetoolsFilledFields";

    public static (JsonElement Fields, bool Changed) Merge(
        JsonElement entityFields, IReadOnlySet<string> allowlist, JsonElement fivetoolsFields)
    {
        var obj = entityFields.ValueKind == JsonValueKind.Object
            ? (JsonObject)JsonNode.Parse(entityFields.GetRawText())!
            : new JsonObject();

        var filled = new SortedSet<string>(StringComparer.Ordinal);
        if (obj[ProvenanceKey] is JsonArray existingProv)
            foreach (var n in existingProv) if (n?.GetValue<string>() is { } s) filled.Add(s);

        var changed = false;
        foreach (var field in allowlist)
        {
            if (fivetoolsFields.ValueKind != JsonValueKind.Object
                || !fivetoolsFields.TryGetProperty(field, out var incoming))
                continue;                                   // 5etools doesn't have it → nothing to fill

            var present = obj.ContainsKey(field);
            if (present && !filled.Contains(field))
                continue;                                   // extraction/human produced it → never touch

            var newVal = JsonNode.Parse(incoming.GetRawText());
            var before = obj[field]?.ToJsonString();
            obj[field] = newVal;
            filled.Add(field);
            if (before != obj[field]?.ToJsonString()) changed = true;
        }

        // Re-write provenance last (sorted) so re-runs are byte-identical.
        obj.Remove(ProvenanceKey);
        if (filled.Count > 0)
            obj[ProvenanceKey] = new JsonArray(filled.Select(f => (JsonNode)f!).ToArray());

        var result = JsonDocument.Parse(obj.ToJsonString()).RootElement.Clone();
        return (result, changed);
    }
}
```

> Note: `changed` tracks whether any value actually changed, so the service can skip writing an unchanged book. The provenance re-write is positioned last for stable ordering.

- [ ] **Step 5: Run tests → PASS.** `dotnet build` → 0/0.

- [ ] **Step 6: Commit**

```bash
git add Features/Ingestion/FivetoolsIngestion/FieldFillAllowlist.cs Features/Ingestion/FivetoolsIngestion/EntityFieldMerger.cs DndMcpAICsharpFun.Tests/Entities/Admin/EntityFieldMergerTests.cs
git commit -m "feat(field-fill): per-type allowlist + fill-missing-only field merger"
```

---

### Task 2: EntityFieldFillService (per-book) + DI

**Files:**
- Create: `Features/Ingestion/FivetoolsIngestion/EntityFieldFillService.cs`
- Modify: `Extensions/ServiceCollectionExtensions.cs` (register the service)
- Test: `DndMcpAICsharpFun.Tests/Entities/Admin/EntityFieldFillServiceTests.cs`

**Interfaces:**
- Consumes: `IngestionRecord` (`.FivetoolsSourceKey`); `EntityIdSlug.BookSlug(key)`; `CanonicalJsonLoader.LoadAsync(path, ct) → CanonicalJsonFile` (`.Entities: IReadOnlyList<EntityEnvelope>`); `CanonicalJsonWriter.WriteAsync(path, CanonicalJsonFile, ct)`; `EntityEnvelope` (`Name`, `Type`, `Fields`, `DataSource`); `FivetoolsSourceRegistry.AllEntries` (`FivetoolsFileEntry{ Path, EntityType, JsonArrayKey }`); `FivetoolsMapperRegistry.Mappers[type].Map(JsonElement) → EntityEnvelope` (its `.Fields` is the 5etools record); `EntityNameIndex.Normalize(name)`; `FieldFillAllowlist.For`, `EntityFieldMerger.Merge` (Task 1); `FivetoolsMapperBase.Edition2024Sources` for edition.
- Produces: `EntityFieldFillService.FillAsync(IngestionRecord record, CancellationToken ct) → FieldFillResult` (`bool HasSourceKey`, `string? CanonicalPath`, `IReadOnlyDictionary<EntityType,int> FilledByType`, `int EntitiesTouched`).

**Context:** For a book with a `FivetoolsSourceKey`: build a per-type index of `Normalize(name) → 5etools Fields` from the roster (only elements whose `source` equals the book's key), using `FivetoolsSourceRegistry.AllEntries` (the file+arrayKey per type) + `FivetoolsMapperRegistry.Mappers[type]` for the fields. Then for each canonical entity whose type has an allowlist and `DataSource != "manual"`, look up its 5etools fields by normalized name and `EntityFieldMerger.Merge`. Write the canonical only if anything changed. No source key → `HasSourceKey=false`, no write. Mirror `EntityBackfillService`'s ctor style (`CanonicalJsonLoader loader, CanonicalJsonWriter writer, string canonicalDirectory, string fivetoolsDirectory`).

- [ ] **Step 1: Write the failing tests** (fake canonical + fake 5etools files on a temp dir; mirror `EntityBackfillServiceTests` for the temp-dir + `IngestionRecord` setup — read it with Serena first)

```csharp
// DndMcpAICsharpFun.Tests/Entities/Admin/EntityFieldFillServiceTests.cs
// Arrange: a temp canonicalDir with <slug>.json holding a prose Class entity ("Barbarian", fields {entries:[]})
//   + a "manual" Class entity; a temp fivetoolsDir/class/class-x.json with class[]={name:"Barbarian",source:<KEY>,hd:{faces:12},classFeatures:[...],subclassTitle:"Primal Path"}.
//   An IngestionRecord with FivetoolsSourceKey=<KEY>.
// Act: await service.FillAsync(record, ct).
// Assert: the Barbarian entity now has hd/classFeatures/subclassTitle + _fivetoolsFilledFields; entries untouched;
//   the "manual" entity is unchanged; result.HasSourceKey true, FilledByType[Class] >= 1.
// Also: a no-source-key record → HasSourceKey false, canonical unchanged.
```

> Fill this in against the real `EntityBackfillServiceTests`/`MonsterBackfillServiceTests` temp-dir helpers (Serena). Confirm the `FivetoolsFileEntry` property names (`Path`/`EntityType`/`JsonArrayKey`) and `EntityIdSlug.BookSlug` with Serena.

- [ ] **Step 2: Run → FAIL** (Docker up for the suite; this test is file-based, no DB).

- [ ] **Step 3: Implement `EntityFieldFillService`** — per the Context. Skeleton:

```csharp
public async Task<FieldFillResult> FillAsync(IngestionRecord record, CancellationToken ct)
{
    var key = record.FivetoolsSourceKey;
    if (string.IsNullOrWhiteSpace(key)) return FieldFillResult.NoSourceKey;

    var slug = EntityIdSlug.BookSlug(key);
    var path = Path.Combine(_canonicalDirectory, slug + ".json");
    if (!File.Exists(path)) return FieldFillResult.NoCanonical(path);

    var file = await _loader.LoadAsync(path, ct);

    // Build per-type 5etools name→Fields index for this book's source key (lazily, only for types present).
    // For each FivetoolsSourceRegistry.AllEntries entry: open Path, read the JsonArrayKey array,
    // for each element whose "source"==key add Normalize(name) → Mappers[type].Map(element).Fields.

    var entities = new List<EntityEnvelope>(file.Entities.Count);
    var filledByType = new Dictionary<EntityType, int>();
    var anyChanged = false;
    foreach (var e in file.Entities)
    {
        var allow = FieldFillAllowlist.For(e.Type);
        if (allow is null || string.Equals(e.DataSource, "manual", StringComparison.Ordinal)
            || !TryGet5etoolsFields(e.Type, e.Name, key, out var fiveFields))
        { entities.Add(e); continue; }

        var (merged, changed) = EntityFieldMerger.Merge(e.Fields, allow, fiveFields);
        entities.Add(changed ? e with { Fields = merged } : e);
        if (changed) { anyChanged = true; filledByType[e.Type] = filledByType.GetValueOrDefault(e.Type) + 1; }
    }

    if (anyChanged)
        await _writer.WriteAsync(path, file with { Entities = entities }, ct);

    return new FieldFillResult(true, path, filledByType, filledByType.Values.Sum());
}
```

Define `FieldFillResult` (record) + the `TryGet5etoolsFields` index helper in the same file. Edition for name-matching is not needed for the index key (name+source is unique within a book), but include the source-key filter exactly like `EntityBackfillService.ComputeAsync`.

- [ ] **Step 4: Register in DI** — add `EntityFieldFillService` to the correct `Add*` group in `Extensions/ServiceCollectionExtensions.cs` with the canonical + 5etools directory args (copy how `EntityBackfillService`/the backfill dictionary is registered — read it with Serena). If `FullContainerScopeValidationTests` covers this group, it's transitively fine; confirm.

- [ ] **Step 5: Run the fill test → PASS.**

- [ ] **Step 6: Canonical-rewrite gates + real-data spot-check** (add to the test file):
  - After `FillAsync`, reload the written canonical with `CanonicalJsonLoader` (round-trips) and assert entity ids are unique.
  - A test that opens the REAL `5etools/class/class-fighter.json` and asserts `class[0]` has `hd` and `classFeatures` — proving the corpus actually carries the allowlisted fields (don't trust the fixture).

- [ ] **Step 7: Build 0/0; full `dotnet test` green. Commit**

```bash
git add Features/Ingestion/FivetoolsIngestion/EntityFieldFillService.cs Extensions/ServiceCollectionExtensions.cs DndMcpAICsharpFun.Tests/Entities/Admin/EntityFieldFillServiceTests.cs
git commit -m "feat(field-fill): per-book EntityFieldFillService + DI + real-5etools gate"
```

---

### Task 3: `fill-fields` admin endpoint

**Files:**
- Modify: `Features/Admin/BooksAdminEndpoints.cs`
- Modify: `DndMcpAICsharpFun.http`, `dnd-mcp-api.insomnia.json`

**Interfaces:**
- Consumes: `EntityFieldFillService.FillAsync` (Task 2); `IIngestionTracker.GetByIdAsync(id, ct)`; the admin-key + `DisableAntiforgery` conventions used by `BackfillEntities`.
- Produces: `POST /admin/books/{id}/fill-fields`.

- [ ] **Step 1: Register the route** in `BooksAdminEndpoints.Map…` next to `backfill-entities`:

```csharp
group.MapPost("/books/{id:int}/fill-fields", FillFields).DisableAntiforgery();
```

- [ ] **Step 2: Add the handler** (mirror `BackfillEntities`):

```csharp
private static async Task<IResult> FillFields(
    int id,
    [FromServices] IIngestionTracker tracker,
    [FromServices] EntityFieldFillService fill,
    CancellationToken ct)
{
    var record = await tracker.GetByIdAsync(id, ct);
    if (record is null) return Results.NotFound($"Book with id {id} not found");

    var result = await fill.FillAsync(record, ct);
    if (result.HasSourceKey && result.CanonicalPath is not null && !File.Exists(result.CanonicalPath))
        return Results.Conflict($"No canonical file found for book {id}; run extraction first.");

    return Results.Ok(new
    {
        hasSourceKey = result.HasSourceKey,
        entitiesTouched = result.EntitiesTouched,
        filledByType = result.FilledByType.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
    });
}
```

- [ ] **Step 3: Update `.http` + `.insomnia`** — add the `POST {{host}}/admin/books/{{bookId}}/fill-fields` example with the `X-Admin-Api-Key` header, mirroring the `backfill-entities` example. Add the matching request to `dnd-mcp-api.insomnia.json`.

- [ ] **Step 4: Build 0/0; full suite green. Commit** (all three files together).

```bash
git add Features/Admin/BooksAdminEndpoints.cs DndMcpAICsharpFun.http dnd-mcp-api.insomnia.json
git commit -m "feat(field-fill): POST /admin/books/{id}/fill-fields endpoint"
```

---

### Task 4: Auto-run the fill when extraction completes

**Files:**
- Modify: the extraction completion site (confirm with Serena — the worker that writes the canonical at the end of `extract-entities`, e.g. `Features/Ingestion/EntityExtraction/EntityExtractionOrchestrator.cs` or the `ExtractEntities` handler in `BooksAdminEndpoints.cs`).
- Test: extend `EntityFieldFillServiceTests` or an extraction-completion test.

**Interfaces:**
- Consumes: `EntityFieldFillService.FillAsync` (Task 2); the extraction worker's post-write hook + its `IngestionRecord`.

**Context:** After extraction writes the canonical for a book, invoke `EntityFieldFillService.FillAsync(record, ct)` so the canonical is enriched before `ingest-entities`. It's a no-op for no-source-key books. Find the exact completion point with Serena (`find_referencing_symbols` on `CanonicalJsonWriter.WriteAsync` within the extraction orchestrator, or the end of the `extract-entities` job).

- [ ] **Step 1: Locate the completion hook** with Serena; identify where the extraction canonical is finalized and the `IngestionRecord` is in scope.
- [ ] **Step 2: Call `FillAsync`** there (inject `EntityFieldFillService`), guarded so a fill failure is logged but does not fail the extraction (extraction already succeeded; the fill is additive). Add a structured log line with the fill report.
- [ ] **Step 3: Test** — a test that runs the extraction-completion path for an official book with a prose Class in its canonical and asserts the canonical afterwards carries the allowlisted fields (deterministic; a second completion is byte-identical).
- [ ] **Step 4: Build 0/0; full suite green. Commit.**

```bash
git commit -m "feat(field-fill): auto-run field-fill when extraction completes"
```

---

### Task 5: One-time cleanup — undo the wholesale ImportAll (operational)

**Not a code build.** Controller-run against the live stack after Tasks 1–4 ship. Restores the extraction-favored index and drops the `dataSource:"5etools"` strays the earlier `POST /admin/5etools/import` wrote.

- [ ] 5.1 **Fill the core books:** `POST /admin/books/{id}/fill-fields` (admin key) for `phb14`, `mm14`, `dmg14`, and any other extracted official book → their canonical classes gain `hd`/`classFeatures`.
- [ ] 5.2 **Rebuild `dnd_entities`:** a `Tools/` console (or a direct Qdrant delete-collection/recreate) that clears the collection, then `POST /admin/books/{id}/ingest-entities` per book — re-projecting the extraction canonical (now field-filled). Not a permanent route. This drops the import strays (books/2024 content extraction never had) and restores extraction entities (`dataSource` back to extraction).
- [ ] 5.3 **Live verify (rebuild the app image first):** the level-up card grounds all 12 classes from **extraction** entities (`dataSource:"llm"` + `fivetoolsFilledFields`), and a monster (e.g. `mm14.monster.aboleth`) reads back as the extraction version — hybrid restored end-to-end. Screenshot the card.

---

## Self-Review

**Spec coverage:**
- "Patches only missing allowlisted structured fields, never overwriting extraction" → Task 1 (`EntityFieldMerger` rules) + `FieldFillAllowlist` (structured only, no `entries`); tests assert entries-untouched + extraction-field-not-overwritten.
- "Type-agnostic via per-type allowlist" → Task 1 (`FieldFillAllowlist`) + Task 2 (service iterates any type with an allowlist); no-allowlist/complete/homebrew = no-op tests.
- "Provenance, idempotent, loses to extraction/human/manual" → Task 1 (`_fivetoolsFilledFields`, re-derive vs untouched) + Task 2 (manual skip); idempotency + byte-identical tests.
- "Auto-runs after extraction, can't decay" → Task 4.
- "`POST /admin/books/{id}/fill-fields`" → Task 3 (+ `.http`/`.insomnia`).
- "Safe canonical write (atomic, unique-id, loadable)" → reuse `CanonicalJsonWriter.WriteAsync` (atomic) + Task 2 Step 6 (unique-id + round-trip gate).

**Placeholder scan:** the "confirm with Serena" notes (Task 2's temp-dir helper + `FivetoolsFileEntry`/`EntityIdSlug.BookSlug` shapes; Task 4's completion site) are grounding checks against real code with concrete fallbacks, not TODO logic.

**Type consistency:** `EntityFieldMerger.Merge(JsonElement, IReadOnlySet<string>, JsonElement) → (JsonElement, bool)` and `FieldFillAllowlist.For(EntityType) → IReadOnlySet<string>?` (Task 1) are consumed unchanged in Task 2; `EntityFieldFillService.FillAsync(IngestionRecord, ct) → FieldFillResult` (Task 2) is consumed unchanged in Tasks 3–5. `_fivetoolsFilledFields` is the single reserved key across merger + tests.
