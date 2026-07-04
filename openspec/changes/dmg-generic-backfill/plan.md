# DMG Generic Backfill Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. All code reads/edits go through Serena symbolic tools (project convention).

**Goal:** Generalize the 5etools recall/backfill machinery into one provider-driven engine covering Monster, Spell, MagicItem, and God, and bring the DMG canonical to recall parity.

**Architecture:** One `EntityBackfillService` owns the recall/diff/flag algorithm (lifted verbatim from `MonsterBackfillService`); a per-`EntityType` `IFivetoolsBackfillProvider` supplies file enumeration + a curated `*Fields` projection. Monster/Spell providers lift the existing curated projections verbatim (parity); MagicItem/God are new. Three type-parameterized admin routes replace the four type-specific ones. The two old services are deleted.

**Tech Stack:** .NET 10, C#, minimal APIs, System.Text.Json, xUnit + FluentAssertions, EF Core (unrelated here), qwen3:8b via Ollama (extraction run only).

## Global Constraints

- Target framework `net10.0`, nullable enabled, **warnings-as-errors** (every project). A build warning fails the task.
- All code reads/edits via **Serena** symbolic tools; call `mcp__serena__initial_instructions` at task start (per project convention).
- Work **directly on `main`** — no feature branch. Commit each task.
- Any HTTP route change updates **both** `DndMcpAICsharpFun.http` and `dnd-mcp-api.insomnia.json` in the same commit.
- Backfilled entities: `dataSource = "5etools-backfill"`, `Disposition = EntityDisposition.Accepted`, `NeedsReview = false`, `CanonicalText = ""` (matches existing Monster/Spell backfill).
- Name diff key is `EntityNameIndex.Normalize(name)` (ordinal).
- Existing test suites `MonsterBackfillServiceTests` / `SpellBackfillServiceTests` are the behavioral contract — their ported forms MUST pass unchanged.

---

### Task 1: Provider seam + generic engine (TDD against a fake provider)

**Files:**
- Create: `Features/Ingestion/FivetoolsIngestion/IFivetoolsBackfillProvider.cs`
- Create: `Features/Ingestion/FivetoolsIngestion/EntityBackfillService.cs`
- Test: `DndMcpAICsharpFun.Tests/Entities/Admin/EntityBackfillServiceTests.cs`

**Interfaces:**
- Produces: `IFivetoolsBackfillProvider`, `EntityBackfillService`, `EntityBackfillResult`, `EntityFlagResult` (consumed by Tasks 2, 3, 4).

```csharp
// IFivetoolsBackfillProvider.cs
public interface IFivetoolsBackfillProvider
{
    EntityType Type { get; }

    /// <summary>Every QUALIFYING 5etools element of this type across all sources (all bestiary/spell files,
    /// or the single global file). MagicItem applies the rarity filter here. Name/source are read by the engine.</summary>
    IEnumerable<JsonElement> EnumerateRoster(string fivetoolsDir);

    /// <summary>Builds a backfill EntityEnvelope for a roster gap: curated *Fields projection,
    /// dataSource "5etools-backfill", Disposition Accepted, id/edition from the book key.</summary>
    EntityEnvelope BuildEntity(string sourceKey, string edition, string name, JsonElement element);
}
```

The engine generalises `MonsterBackfillService.ComputeAsync` + `FlagUnknownAsync` verbatim, replacing the hardcoded `EnumerateFivetoolsMonsters`/`BuildEntity` with `provider.EnumerateRoster`/`provider.BuildEntity`, and keying element name via `element.GetProperty("name")` and source via `element.TryGetProperty("source", ...)`.

- [ ] **Step 1: Write the failing engine tests (fake provider).** Create `EntityBackfillServiceTests` with an in-test `FakeProvider : IFivetoolsBackfillProvider` (Type = `EntityType.Monster`) whose `EnumerateRoster` yields hand-built `JsonElement`s (parse from a JSON string: names "Goblin" source "MM", "Bullywug" source "MM", "Aboleth" source "MPMM") and whose `BuildEntity` returns a minimal envelope (`new EntityEnvelope(Id: EntityIdSlug.For(sourceKey, Type, name), Type, name, sourceKey, edition, null, new FirstAppearance(sourceKey, edition, null), Array.Empty<Revision>(), Array.Empty<string>(), "", default, "5etools-backfill", false, false, false, false, Array.Empty<string>(), EntityDisposition.Accepted)`). Seed a temp canonical (Goblin grounded; "Vault Guardian" grounded unknown-extra; "Ghost Recall" backfilled unknown-extra; "Aboleth" grounded other-source) exactly like `MonsterBackfillServiceTests`. Assert: Bullywug in `Missing`+`ToAppend`; Goblin `AlreadyPresent`; Aboleth excluded from Missing; `GroundedCount==3`, `BackfilledCount==1`; `Extra` = {Vault Guardian, Ghost Recall, Aboleth}; `ExtraOtherSource`={Aboleth}; `ExtraUnknown`={Vault Guardian, Ghost Recall}; idempotency after apply; no-source-key no-op; and `FlagUnknownAsync` flags the two unknowns, not Aboleth, deletes nothing, idempotent.

- [ ] **Step 2: Run the tests — verify they fail to compile.** Run: `dotnet test DndMcpAICsharpFun.Tests --filter FullyQualifiedName~EntityBackfillServiceTests`  Expected: build FAIL (`EntityBackfillService`/`IFivetoolsBackfillProvider` not defined).

- [ ] **Step 3: Create `IFivetoolsBackfillProvider`** (code above).

- [ ] **Step 4: Create `EntityBackfillService`.** Copy `MonsterBackfillService.cs` in full to `EntityBackfillService.cs`; rename the class and result records to `EntityBackfillService` / `EntityBackfillResult` / `EntityFlagResult`; add a `private readonly IFivetoolsBackfillProvider _provider;` ctor param (keep `BookSourceRegistry`, `CanonicalJsonLoader`, `canonicalDirectory`, `fivetoolsDirectory`). In `ComputeAsync`/`FlagUnknownAsync` replace `e.Type != EntityType.Monster` with `e.Type != _provider.Type`; replace the `EnumerateFivetoolsMonsters(null)` loop source with `_provider.EnumerateRoster(_fivetoolsDirectory)`; replace `BuildEntity(key, edition, name, monster)` with `_provider.BuildEntity(key, edition, name, monster)`. Delete the monster-specific `EnumerateFivetoolsMonsters`, `BuildEntity`, `GetKeywords`, `MonsterFieldNames`, `BuildFields`, `CopyOrNull` (they move to the Monster provider in Task 2).

- [ ] **Step 5: Run the tests — verify PASS.** Run: `dotnet test DndMcpAICsharpFun.Tests --filter FullyQualifiedName~EntityBackfillServiceTests`  Expected: PASS.

- [ ] **Step 6: Commit.**
```bash
git add Features/Ingestion/FivetoolsIngestion/IFivetoolsBackfillProvider.cs Features/Ingestion/FivetoolsIngestion/EntityBackfillService.cs DndMcpAICsharpFun.Tests/Entities/Admin/EntityBackfillServiceTests.cs
git commit -m "feat(backfill): generic EntityBackfillService + provider seam (TDD, fake provider)"
```

---

### Task 2: Monster + Spell providers (lift curated projections verbatim)

**Files:**
- Create: `Features/Ingestion/FivetoolsIngestion/Providers/MonsterBackfillProvider.cs`
- Create: `Features/Ingestion/FivetoolsIngestion/Providers/SpellBackfillProvider.cs`
- Test: `DndMcpAICsharpFun.Tests/Entities/Admin/BackfillProviderTests.cs`

**Interfaces:**
- Consumes: `IFivetoolsBackfillProvider` (Task 1).
- Produces: `MonsterBackfillProvider`, `SpellBackfillProvider` (consumed by Tasks 3, 4).

- [ ] **Step 1: Write failing provider tests.** In `BackfillProviderTests`, for `MonsterBackfillProvider`: call `BuildEntity("MM","Edition2014","Bullywug", element)` on the Bullywug JSON from `MonsterBackfillServiceTests` and assert Id `mm14.monster.bullywug`, `DataSource=="5etools-backfill"`, `Disposition==Accepted`, `Srd==true`, `Keywords` contains "Amphibious", and `loader.DeserialiseFields<MonsterFields>(entity)` gives Str=12, Hp.Average=11, Senses contains "darkvision 60 ft.". For `SpellBackfillProvider`: build from a spell JSON `{ "name":"Regenerate","source":"PHB","page":1,"entries":["Heals."],"entriesHigherLevel":[...],"damageInflict":["fire"] }` and assert `DeserialiseFields<SpellFields>` has the `Description` block wrapping `["Heals."]`, `damageInflict` copied.

- [ ] **Step 2: Run — verify fail.** Run: `dotnet test DndMcpAICsharpFun.Tests --filter FullyQualifiedName~BackfillProviderTests`  Expected: build FAIL (providers not defined).

- [ ] **Step 3: Implement `MonsterBackfillProvider`.** `Type => EntityType.Monster`. `EnumerateRoster(dir)` = the exact body of `MonsterBackfillService.EnumerateFivetoolsMonsters(sourceKey: null)` (bestiary-*.json, exclude fluff/index/foundry, per-file JsonException skip, `el.Clone()`). `BuildEntity(sourceKey, edition, name, element)` + private `BuildFields`/`GetKeywords`/`MonsterFieldNames`/`CopyOrNull` = the exact bodies removed from `MonsterBackfillService` in Task 1 Step 4, with `BuildEntity`'s signature taking `name` explicitly and its `Id`/`Edition` using `sourceKey`/`edition` params. (Verbatim lift — no logic change.)

- [ ] **Step 4: Implement `SpellBackfillProvider`.** `Type => EntityType.Spell`. `EnumerateRoster(dir)` = `SpellBackfillService.EnumerateFivetoolsSpells` but **all-source** (remove the `source == sourceKey` filter — the engine filters by source; keep the fluff/index/foundry file exclusion). `BuildEntity` + `BuildFields`/`CopyOrEmptyArray`/`CopyOrNull` = the exact bodies from `SpellBackfillService`, signature taking `name`, id/edition from params.

- [ ] **Step 5: Run — verify PASS.** Run: `dotnet test DndMcpAICsharpFun.Tests --filter FullyQualifiedName~BackfillProviderTests`  Expected: PASS.

- [ ] **Step 6: Commit.**
```bash
git add Features/Ingestion/FivetoolsIngestion/Providers/ DndMcpAICsharpFun.Tests/Entities/Admin/BackfillProviderTests.cs
git commit -m "feat(backfill): Monster + Spell providers (curated projection lifted verbatim)"
```

---

### Task 3: MagicItem + God providers (new curated projections)

**Files:**
- Create: `Features/Ingestion/FivetoolsIngestion/Providers/MagicItemBackfillProvider.cs`
- Create: `Features/Ingestion/FivetoolsIngestion/Providers/GodBackfillProvider.cs`
- Test: extend `DndMcpAICsharpFun.Tests/Entities/Admin/BackfillProviderTests.cs`

**Interfaces:**
- Produces: `MagicItemBackfillProvider`, `GodBackfillProvider` (consumed by Tasks 3-DI/4).

Field mappings (5etools → canonical), from confirmed 5etools shapes:
- **MagicItem** → `MagicItemFields(Rarity, ItemCategory, Attunement, Description)`: `Rarity` = `rarity` ?? ""; `ItemCategory` = `type` with any `"|SOURCE"` suffix stripped ?? ""; `Attunement` = `reqAttune` as string when string, `"requires attunement"` when `true`, else ""; `Description` = string `entries[]` joined with `"\n\n"` (skip non-string entries).
- **God** → `GodFields(Alignment, Domains, Symbol?, Pantheon?, Plane?, Description)`: `Alignment` = `alignment[]` joined with `", "` ?? ""; `Domains` = `domains[]` ?? []; `Symbol`/`Pantheon`/`Plane` = string or null; `Description` = string `entries[]` joined ?? "".

- [ ] **Step 1: Write failing tests.** For MagicItem: build from `{ "name":"+1 Rod of the Pact Keeper","source":"DMG","rarity":"uncommon","type":"RD|DMG","reqAttune":"by a warlock","entries":["While holding this rod..."] }`; assert `DeserialiseFields<MagicItemFields>` → Rarity "uncommon", ItemCategory "RD", Attunement "by a warlock", Description starts "While holding". Also assert `EnumerateRoster` on a fixture `items.json` with one magic item (rarity "rare") + one mundane (rarity "none") yields ONLY the magic item. For God: build from the Asmodeus JSON `{ "name":"Asmodeus","source":"DMG","alignment":["L","E"],"domains":["Trickery","Order"],"symbol":"Three triangles","pantheon":"Dawn War" }`; assert Alignment "L, E", Domains has "Trickery", Symbol set, Plane null, Description "".

- [ ] **Step 2: Run — verify fail.** Run: `dotnet test DndMcpAICsharpFun.Tests --filter FullyQualifiedName~BackfillProviderTests`  Expected: build FAIL.

- [ ] **Step 3: Implement `MagicItemBackfillProvider`.** `Type => EntityType.MagicItem`. `EnumerateRoster(dir)` reads `{dir}/items.json`, `"item"` array, yields `el.Clone()` for each element with a `rarity` string ≠ `"none"` (case-insensitive); per-file JsonException guard. `BuildEntity` sets Id `EntityIdSlug.For(sourceKey, EntityType.MagicItem, name)`, page from `page`, srd flags, `Fields` = the `MagicItemFields` projection above serialised to `JsonElement` (build a `JsonObject{ ["rarity"]=..., ["itemCategory"]=..., ["attunement"]=..., ["description"]=... }` — property names must match `MagicItemFields` JSON binding; verify with `loader.DeserialiseFields<MagicItemFields>`), `DataSource="5etools-backfill"`, `Disposition=Accepted`, `NeedsReview=false`, `Keywords=Array.Empty<string>()`.

- [ ] **Step 4: Implement `GodBackfillProvider`.** `Type => EntityType.God`. `EnumerateRoster(dir)` reads `{dir}/deities.json`, `"deity"` array, yields all `el.Clone()`. `BuildEntity` builds `GodFields` projection into a `JsonObject` (`alignment`, `domains`, `symbol`, `pantheon`, `plane`, `description`), matching `GodFields` JSON binding.

- [ ] **Step 5: Run — verify PASS.** Run: `dotnet test DndMcpAICsharpFun.Tests --filter FullyQualifiedName~BackfillProviderTests`  Expected: PASS.

- [ ] **Step 6: Commit.**
```bash
git add Features/Ingestion/FivetoolsIngestion/Providers/ DndMcpAICsharpFun.Tests/Entities/Admin/BackfillProviderTests.cs
git commit -m "feat(backfill): MagicItem + God providers (new curated *Fields projection)"
```

---

### Task 4: Port old-service tests onto the engine; delete old services; DI

**Files:**
- Modify: `DndMcpAICsharpFun.Tests/Entities/Admin/MonsterBackfillServiceTests.cs` → engine-based
- Modify: `DndMcpAICsharpFun.Tests/Entities/Admin/SpellBackfillServiceTests.cs` → engine-based
- Delete: `Features/Ingestion/FivetoolsIngestion/MonsterBackfillService.cs`, `SpellBackfillService.cs`
- Modify: `Extensions/ServiceCollectionExtensions.cs:222-232`

**Interfaces:**
- Consumes: `EntityBackfillService`, providers (Tasks 1-3).
- Produces: DI registrations `EntityBackfillService` keyed by type, or a `Func<EntityType, EntityBackfillService>` factory / `IReadOnlyDictionary<EntityType, EntityBackfillService>` (consumed by Task 5 endpoints).

- [ ] **Step 1: Port the Monster test suite.** In `MonsterBackfillServiceTests`, replace `new MonsterBackfillService(registry, _loader, _canonicalDir, _fivetoolsDir)` with `new EntityBackfillService(new MonsterBackfillProvider(), registry, _loader, _canonicalDir, _fivetoolsDir)` and the field type to `EntityBackfillService`. Leave every assertion unchanged. Do the same for `SpellBackfillServiceTests` with `new SpellBackfillProvider()`. (Ctor arg order per Task 1 Step 4 — provider first.)

- [ ] **Step 2: Run — expect PASS (contract preserved).** Run: `dotnet test DndMcpAICsharpFun.Tests --filter "FullyQualifiedName~MonsterBackfillServiceTests|FullyQualifiedName~SpellBackfillServiceTests"`  Expected: PASS. If any fail, the provider lift diverged — fix the provider, not the test.

- [ ] **Step 3: Delete the old services.** Remove `MonsterBackfillService.cs` and `SpellBackfillService.cs` via Serena. Grep for any remaining references: `mcp__serena__search_for_pattern "MonsterBackfillService|SpellBackfillService"` — expect only the (about-to-change) DI + endpoint files.

- [ ] **Step 4: Update DI.** In `ServiceCollectionExtensions.cs`, replace the two `AddSingleton<...BackfillService>` blocks (lines ~222-232) with registrations of the four providers and a resolver. Register each provider as `IFivetoolsBackfillProvider` (or by concrete type) and add:
```csharp
services.AddSingleton<IReadOnlyDictionary<EntityType, EntityBackfillService>>(sp =>
{
    var registry = sp.GetRequiredService<BookSourceRegistry>();
    var loader = sp.GetRequiredService<CanonicalJsonLoader>();
    var canonicalDir = sp.GetRequiredService<IOptions<EntityExtractionOptions>>().Value.CanonicalDirectory;
    var fivetoolsDir = configuration["EntityExtraction:FivetoolsDataDirectory"] ?? "5etools";
    IFivetoolsBackfillProvider[] providers =
        { new MonsterBackfillProvider(), new SpellBackfillProvider(), new MagicItemBackfillProvider(), new GodBackfillProvider() };
    return providers.ToDictionary(p => p.Type, p => new EntityBackfillService(p, registry, loader, canonicalDir, fivetoolsDir));
});
```

- [ ] **Step 5: Build — verify 0 warnings/errors.** Run: `dotnet build`  Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 6: Commit.**
```bash
git add -A
git commit -m "refactor(backfill): port Monster/Spell tests onto engine; delete old services; DI by type"
```

---

### Task 5: Type-parameterized endpoints + API contracts

**Files:**
- Modify: `Features/Admin/BooksAdminEndpoints.cs` (routes ~31-34; handlers ~150-270)
- Modify: `DndMcpAICsharpFun.http`, `dnd-mcp-api.insomnia.json`
- Test: `DndMcpAICsharpFun.Tests/Entities/Admin/` — rename/rework `BackfillSpellsEndpointTests.cs` → `EntityBackfillEndpointTests.cs`

**Interfaces:**
- Consumes: `IReadOnlyDictionary<EntityType, EntityBackfillService>` (Task 4).

- [ ] **Step 1: Write failing endpoint tests.** In `EntityBackfillEndpointTests` (model on `BackfillSpellsEndpointTests`, registering the provider-dictionary in the test host): assert `GET /admin/books/1/entity-recall?type=Monster` 200 with the recall shape; `POST /admin/books/1/backfill-entities?type=Spell` appends; `POST /admin/books/1/flag-unknown-entities?type=Monster` flags; `?type=Plane` → 400; missing-source-key → 400.

- [ ] **Step 2: Run — verify fail.** Run: `dotnet test DndMcpAICsharpFun.Tests --filter FullyQualifiedName~EntityBackfillEndpointTests`  Expected: FAIL (routes not present).

- [ ] **Step 3: Replace routes.** In `MapBooksAdmin`, remove the four lines (`backfill-spells`, `monster-recall`, `backfill-monsters`, `flag-unknown-monsters`) and add:
```csharp
group.MapGet("/books/{id:int}/entity-recall", EntityRecall);
group.MapPost("/books/{id:int}/backfill-entities", BackfillEntities).DisableAntiforgery();
group.MapPost("/books/{id:int}/flag-unknown-entities", FlagUnknownEntities).DisableAntiforgery();
```

- [ ] **Step 4: Implement the three handlers.** Each takes `int id, [FromQuery] string type, [FromServices] IReadOnlyDictionary<EntityType, EntityBackfillService> services, ...`. Parse `type` with `Enum.TryParse<EntityType>(type, ignoreCase:true, out var et) && services.TryGetValue(et, out var svc)` — else `Results.Problem($"Unsupported type '{type}'. Supported: Monster, Spell, MagicItem, God.", statusCode:400)`. Then reuse the exact bodies of the old `MonsterRecall` (recall response shape), `BackfillMonsters` (load→concat→write; `backfilled`/`alreadyPresent` response), `FlagUnknownMonsters` (`FlagUnknownAsync`; `flagged`/`flaggedCount`) against `svc`. Remove the four old handler methods.

- [ ] **Step 5: Run tests + build.** Run: `dotnet test DndMcpAICsharpFun.Tests --filter FullyQualifiedName~EntityBackfillEndpointTests` then `dotnet build`.  Expected: PASS; 0 warnings.

- [ ] **Step 6: Update API contracts.** In `DndMcpAICsharpFun.http` remove the four old example requests and add `entity-recall?type=Monster`, `backfill-entities?type=MagicItem`, `flag-unknown-entities?type=Monster` (+ one showing a 400 for an unsupported type). Mirror the same additions/removals in `dnd-mcp-api.insomnia.json`.

- [ ] **Step 7: Commit.**
```bash
git add -A
git commit -m "feat(admin): type-parameterized entity-recall/backfill/flag routes; update .http + insomnia"
```

---

### Task 6: DMG coverage run (data) + finish

**Files:** `books/canonical/dmg14.json` (+ siblings) — regenerated data, not code.

Preconditions: Ollama up (`curl -s localhost:11434/api/tags`), app stack reachable. Admin auth header `X-Admin-Api-Key` (value = `Admin__ApiKey`, git-crypt'd — do not echo).

- [ ] **Step 1: Confirm DMG registration.** `GET /admin/books` → find the DMG record; confirm `fivetoolsSourceKey == "DMG"`. If absent, register/set it per CLAUDE.md's add-a-book flow before extracting.

- [ ] **Step 2: Re-extract DMG.** `POST /admin/books/{dmgId}/extract-entities?force=true`. Poll status; the run is checkpointed (`dmg14.progress*.json`) and resumable. When done, review the `git diff` of `dmg14.json`: bogus "Class" entities gone (expect ≈0, was 14), names clean, `dmg14.errors.json` shrunk. Hand-correct obvious LLM mistakes. Commit the corrected canonical.

- [ ] **Step 3: Recall report.** For each of `Monster, Spell, MagicItem, God`: `GET /admin/books/{dmgId}/entity-recall?type={t}` and record `present/missing/extra/grounded/backfilled`. Sanity: MagicItem roster ≈372, God ≈20.

- [ ] **Step 4: Backfill + flag.** For each of the four types: `POST backfill-entities?type={t}`, then `POST flag-unknown-entities?type={t}`. Re-run recall to confirm `missing` shrank to 0 (or explained residual). Commit the corrected `dmg14.json`.

- [ ] **Step 5: Validate.** `POST /admin/canonical/validate` → confirm zero FAIL-class issues attributable to `dmg14` (pre-existing SRD dangling-ref warnings elsewhere are not DMG failures).

- [ ] **Step 6: Full verify.** `dotnet build` (0/0) and `dotnet test` non-persistence suite green. Whole-change reviewer subagent (opus, Serena-driven) before final sign-off.

- [ ] **Step 7 (DEFERRED — separate, needs stack): `dnd_entities` re-ingest.** Once the Ollama-backed stack is up: `POST /admin/books/{dmgId}/ingest-entities` to project cleaned `dmg14` into `dnd_entities`. NOT part of this change's code commits.

---

### Task 7: Rich entry flattening for MagicItem + God Description

**Files:**
- Create: `Features/Ingestion/FivetoolsIngestion/FivetoolsEntryText.cs`
- Modify: `Features/Ingestion/FivetoolsIngestion/Providers/MagicItemBackfillProvider.cs`, `GodBackfillProvider.cs`
- Test: `DndMcpAICsharpFun.Tests/Entities/Admin/FivetoolsEntryTextTests.cs`

**Interfaces:**
- Produces: `static string FivetoolsEntryText.Flatten(JsonElement entriesArray)` (consumed by both providers).

Current MagicItem/God `Description` joins only top-level string `entries`. This task replaces that with a recursive flattener.

- [ ] **Step 1: Write failing tests.** In `FivetoolsEntryTextTests`, parse a JsonElement array containing: a plain string; a `{"type":"entries","name":"Sub","entries":["inner"]}`; a `{"type":"list","items":["a","b"]}`; a `{"type":"table","colLabels":["d100","Result"],"rows":[["01-50","Water"],["51-00","Beer"]]}`; and a string with an inline tag `"Roll {@dice 1d4} then {@item Rope|PHB}."`. Assert `Flatten` returns text that: keeps the plain string; includes "inner" (optionally "Sub" header); has bulleted "a"/"b"; includes "d100 | Result" and "01-50 | Water"; and reduces the tags to "Roll 1d4 then Rope." Assert an empty/absent array → "".

- [ ] **Step 2: Run — fail.** `dotnet test DndMcpAICsharpFun.Tests --filter FullyQualifiedName~FivetoolsEntryTextTests` (sandbox disabled if `.gitmodules` access error). Expected: build FAIL (Flatten undefined).

- [ ] **Step 3: Implement `FivetoolsEntryText.Flatten`.** Iterate the array; per element by `ValueKind`: `String` → strip inline tags then append; `Object` switch on `type`: `"entries"`/`"section"` → optional `name` as a line then recurse `entries`; `"list"` → each `items` element via `"• " + FlattenInline`; `"table"` → `string.Join(" | ", colLabels)` header line + each `rows[i]` (array) cells joined `" | "` (recursing/stripping cell strings); `"item"`/`"itemSpell"` → `name` + `entry`/`entries`; default → if it has `entries`/`entry` recurse, else skip. Join top-level pieces with `"\n\n"`. Inline-tag strip: regex replace `\{@\w+\s+([^}|]+)(\|[^}]*)?\}` → `$1` (display text before any `|`), applied to every string. Keep it a single static class, no state.

- [ ] **Step 4: Rewire providers.** In `MagicItemBackfillProvider` and `GodBackfillProvider`, replace the top-level-strings Description join with `FivetoolsEntryText.Flatten(entriesElement)` (guard when `entries` absent → "").

- [ ] **Step 5: Run tests + the existing provider tests.** `dotnet test DndMcpAICsharpFun.Tests --filter "FullyQualifiedName~FivetoolsEntryTextTests|FullyQualifiedName~BackfillProviderTests"` (sandbox disabled). Expected PASS — update any BackfillProviderTests Description assertion that assumed the old top-level-only behavior to the richer output (the round-trip still deserialises as `MagicItemFields`/`GodFields`). `dotnet build` 0/0.

- [ ] **Step 6: Commit.**
```bash
git add Features/Ingestion/FivetoolsIngestion/FivetoolsEntryText.cs Features/Ingestion/FivetoolsIngestion/Providers/MagicItemBackfillProvider.cs Features/Ingestion/FivetoolsIngestion/Providers/GodBackfillProvider.cs DndMcpAICsharpFun.Tests/Entities/Admin/FivetoolsEntryTextTests.cs DndMcpAICsharpFun.Tests/Entities/Admin/BackfillProviderTests.cs
git commit -m "feat(backfill): rich recursive entry flattening for MagicItem + God Description"
```

---

### Task 8: +N magic-item variant expansion into the MagicItem roster

**Files:**
- Create: `Features/Ingestion/FivetoolsIngestion/MagicVariantExpander.cs`
- Modify: `Features/Ingestion/FivetoolsIngestion/Providers/MagicItemBackfillProvider.cs`
- Test: `DndMcpAICsharpFun.Tests/Entities/Admin/MagicVariantExpanderTests.cs`

**Interfaces:**
- Consumes: `FivetoolsEntryText` (Task 7) is unaffected; the expander emits synthetic 5etools-shaped `JsonElement`s that the existing `MagicItemBackfillProvider.BuildEntity`/rarity-filter already handle.
- Produces: `IEnumerable<JsonElement> MagicVariantExpander.Expand(string fivetoolsDir)` — synthetic magic items across ALL sources, each tagged with its `inherits.source`.

The expander generates the templated `+N` items that live in `magicvariants.json` (not `items.json`), so recall matches extracted "+1 Longsword" and backfill fills the gaps.

- [ ] **Step 1: Write failing tests.** In `MagicVariantExpanderTests`, write a temp `magicvariants.json` with one variant: `{"name":"+1 Weapon","inherits":{"namePrefix":"+1 ","source":"DMG","rarity":"uncommon","bonusWeapon":"+1","entries":["You have a {=bonusWeapon} bonus to attack and damage rolls made with this magic weapon."]},"requires":[{"weapon":true}]}`, and a temp `items-base.json` with `{"baseitem":[{"name":"Longsword","source":"PHB","weapon":true,"type":"M|PHB"},{"name":"Torch","source":"PHB"}]}`. Assert `Expand(dir)` yields a synthetic item with `name`=="+1 Longsword", `source`=="DMG", `rarity`=="uncommon", and `entries[0]` containing "+1 bonus to attack" (placeholder substituted), and does NOT yield a variant for "Torch" (no `weapon:true`). Also assert an `excludes` predicate suppresses a match, and a variant with a different `inherits.source` is tagged with THAT source.

- [ ] **Step 2: Run — fail.** `dotnet test DndMcpAICsharpFun.Tests --filter FullyQualifiedName~MagicVariantExpanderTests` (sandbox disabled). Expected: build FAIL.

- [ ] **Step 3: Implement `MagicVariantExpander.Expand(dir)`.** Read `{dir}/magicvariants.json` `"magicvariant"[]` (JsonException guard → empty). Read the base-item pool from `{dir}/items-base.json` `"baseitem"[]` (guard). For each variant: read `inherits` (skip if absent); for each base item, `Matches(baseItem, requires, excludes)`: `requires` is a list — matches if ANY entry matches; an entry matches if EVERY key/value pair matches the base item (`type` compared after stripping `|SOURCE`; boolean/string/number compared by value); `excludes` (if present) is a list — if ANY entry matches, reject. For a matching base item, build a `JsonObject`: `name` = `(namePrefix??"") + baseName + (nameSuffix??"")`; `source` = `inherits.source`; copy `page`,`rarity`,`reqAttune`,`srd`,`tier` from `inherits` when present; `type` = base item's `type`; `entries` = `inherits.entries` deep-copied with placeholders substituted — replace every `{=key}` or `{=key/mod}` token with `inherits[key]`'s string value (or `baseName` for `{=baseName}`; empty string when the key is absent). Yield each as a cloned `JsonElement`. No state; pure function of the files.

- [ ] **Step 4: Wire into the roster.** In `MagicItemBackfillProvider.EnumerateRoster(dir)`, after yielding the real `items.json` magic items, also yield `MagicVariantExpander.Expand(dir)` results that pass the same rarity filter (variants carry `inherits.rarity`, so they qualify). The synthetic items carry `source`, so the engine's source filter and the otherSource/unknown split treat them correctly.

- [ ] **Step 5: Run tests + provider/engine tests.** `dotnet test DndMcpAICsharpFun.Tests --filter "FullyQualifiedName~MagicVariantExpanderTests|FullyQualifiedName~BackfillProviderTests|FullyQualifiedName~EntityBackfillServiceTests"` (sandbox disabled). Expected PASS. `dotnet build` 0/0.

- [ ] **Step 6: Commit.**
```bash
git add Features/Ingestion/FivetoolsIngestion/MagicVariantExpander.cs Features/Ingestion/FivetoolsIngestion/Providers/MagicItemBackfillProvider.cs DndMcpAICsharpFun.Tests/Entities/Admin/MagicVariantExpanderTests.cs
git commit -m "feat(backfill): expand +N magicvariants into the MagicItem roster"
```

---

### Task 9: Refresh API contracts note

- [ ] **Step 1: Update `.http` + `.insomnia.json`.** No route change, but add a comment on the `backfill-entities?type=MagicItem` example noting it now includes rich-flattened Descriptions and expanded `+N` variants; re-verify `DndMcpAICsharpFun.http` and `dnd-mcp-api.insomnia.json` are in sync. Commit `docs(http): note MagicItem backfill covers +N variants + rich descriptions`.

---

## Self-Review

- **Spec coverage:** entity-recall (Task 5 §3-4), backfill via provider (Tasks 1-5), MagicItem roster rule (Task 3), extra categorization + flag-unknown (Task 1 engine, Task 5 endpoint), DMG re-extraction + parity + validation (Task 6). All spec requirements mapped.
- **Placeholder scan:** none — every new type's projection and each route is spelled out; verbatim lifts name their exact source method.
- **Type consistency:** `EntityBackfillService(IFivetoolsBackfillProvider, BookSourceRegistry, CanonicalJsonLoader, string, string)` ctor used identically in Task 1 fake test, Task 4 DI + ported tests. Result records `EntityBackfillResult`/`EntityFlagResult` produced in Task 1, consumed by Task 5 handlers with the same field names as the old `MonsterBackfillResult` (present/grounded/backfilled/missing/extra/extraOtherSource/extraUnknown; flagged).
