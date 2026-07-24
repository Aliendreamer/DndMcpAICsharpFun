# Race Ability Filter — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Filter entity retrieval by a race's ability-score bonus (`abilityBonus=str`), matching fixed AND choosable bonuses, via a query-time in-memory match like `castableByClass` — no re-index.

**Architecture:** A pure `RaceAbilityParser` reads the boosted-ability set from an entity's raw `Fields` JsonElement. `EntityRetrievalService` gains a `ListRacesByAbilityAsync` mirroring `ListSpellsByClassAsync`. The `abilityBonus` param threads through `EntitySearchQuery` and `GET /retrieval/entities/list`.

**Tech Stack:** C# / .NET 10, xunit + FluentAssertions, real-Qdrant integration via `QdrantFixture`. Serena for all tracked files.

## Global Constraints
- **Serena only** for tracked files (subagents too; `.superpowers/` report = built-in Write only; stop after 1 >2min hang).
- **Work on `main`**; commit each reviewed task. Build 0/0. `dotnet` needs `dangerouslyDisableSandbox: true`. Ignore LSP false CS0246 on tests.
- **HTTP contract change** (new query param on `/retrieval/entities/list`) → update `DndMcpAICsharpFun.http` AND `dnd-mcp-api.insomnia.json` in the same commit (Task 3).
- Query-time filter (NOT a payload field): do NOT add `AbilityBonus` to `BuildFilters`/`EntityFilters`. No re-index, no GPU, no migration.
- Grounding: a race with no structured ability data simply doesn't match — never fabricated. Parser never throws.
- Scope: ability bonus only (resistance/immunity + any chat/MCP tool are OUT).

**Known shapes (verified):**
```csharp
// Domain/Entities/EntityEnvelope.cs — record with `JsonElement Fields` (raw canonical fields; 5etools lowercase keys).
//   Its typed accessor CanonicalJsonLoader.DeserialiseFields<T> THROWS on shape drift — do NOT use it here; parse the raw JsonElement.
// EntitySearchQuery (Features/Retrieval/Entities/EntitySearchQuery.cs): positional record ending
//   `bool? Srd = null, bool? Srd52 = null, bool? BasicRules2024 = null, string? CastableByClass = null`.
// EntityRetrievalService (ctor `(IEntityVectorStore store, ...)`), consts DefaultSetCap=50, MaxSetCap=200, SpellClassMaxScan=3000.
//   The list method (contains the CastableByClass branch): `var clamped = Math.Clamp(...); if (!string.IsNullOrWhiteSpace(q.CastableByClass)) return await ListSpellsByClassAsync(q, clamped, ct); var (total, hits) = await store.ListByFilterAsync(BuildFilters(q), clamped, ct); ...`
//   ListSpellsByClassAsync: `BuildFilters(q) with { Type = EntityType.Spell }; (_, hits) = ListByFilterAsync(spellFilters, SpellClassMaxScan, ct); matched = hits.Where(h => spellClassIndex.CanCast(q.CastableByClass!, h.Envelope.Name, h.Envelope.SourceBook)).ToList(); rows = matched.Take(cap).Select(h => ToRow(h.Envelope)).ToList(); return new EntitySetResult(matched.Count, rows.Count, rows);`
//   ToRow(envelope) exists; hits expose `h.Envelope` (EntityEnvelope, incl. Fields — VERIFY populated on list hits, see Task 2/4).
// EntityRetrievalEndpoints.ListPublic(... string? castableByClass, IEntityRetrievalService svc, CancellationToken ct, int limit=50)
//   -> BuildQuery(...) with { CastableByClass = castableByClass }; then svc.ListAsync(query, limit, ct).
// .http entities/list examples at lines ~269-275 (…?type=Monster…, …?castableByClass=Wizard…). QdrantFixture: DndMcpAICsharpFun.Tests/VectorStore/Entities/QdrantFixture.cs (+ QdrantEntityVectorStoreScrollTests for the seed/scroll pattern).
```

---

## Task 1: `RaceAbilityParser`

**Files:**
- Create: `Features/Retrieval/Entities/RaceAbilityParser.cs`
- Test: `DndMcpAICsharpFun.Tests/Retrieval/RaceAbilityParserTests.cs`

**Interfaces:**
- Produces: `public static IReadOnlySet<string> RaceAbilityParser.BoostedAbilities(JsonElement fields)`.

- [ ] **Step 1: Write failing tests.** Feed raw `JsonElement`s (via `JsonDocument.Parse(...).RootElement`).
```csharp
using System.Text.Json;
using DndMcpAICsharpFun.Features.Retrieval.Entities;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Retrieval;

public sealed class RaceAbilityParserTests
{
    private static JsonElement Fields(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Fixed_bonus_keys()
        => RaceAbilityParser.BoostedAbilities(Fields("""{"ability":[{"str":2,"con":1}]}"""))
            .Should().BeEquivalentTo(["str", "con"]);

    [Fact]
    public void Choosable_bonus_included()
        => RaceAbilityParser.BoostedAbilities(Fields("""{"ability":[{"choose":{"from":["str","dex","con"],"count":1}}]}"""))
            .Should().Contain("str").And.Contain("dex").And.Contain("con");

    [Fact]
    public void Mixed_fixed_and_choose()
        => RaceAbilityParser.BoostedAbilities(Fields("""{"ability":[{"cha":2,"choose":{"from":["str","wis"],"count":1}}]}"""))
            .Should().BeEquivalentTo(["cha", "str", "wis"]);

    [Fact]
    public void No_ability_data_is_empty()
        => RaceAbilityParser.BoostedAbilities(Fields("""{"size":["M"]}""")).Should().BeEmpty();

    [Fact]
    public void Case_tolerant_ability_key_and_malformed_is_ignored_not_thrown()
    {
        RaceAbilityParser.BoostedAbilities(Fields("""{"Ability":[{"str":2}]}""")).Should().Contain("str"); // case-tolerant key
        RaceAbilityParser.BoostedAbilities(Fields("""{"ability":"nonsense"}""")).Should().BeEmpty();        // wrong kind → no throw
        RaceAbilityParser.BoostedAbilities(Fields("""[1,2,3]""")).Should().BeEmpty();                       // not an object → no throw
    }
}
```

- [ ] **Step 2: Run → FAIL** (type missing).
Run: `dotnet test --filter "FullyQualifiedName~RaceAbilityParser"` (dangerouslyDisableSandbox). Expected: does not compile.

- [ ] **Step 3: Implement** `Features/Retrieval/Entities/RaceAbilityParser.cs`:
```csharp
using System.Text.Json;

namespace DndMcpAICsharpFun.Features.Retrieval.Entities;

/// <summary>
/// The set of ability codes (str/dex/con/int/wis/cha) a race boosts, parsed from its raw entity
/// <c>Fields</c> — fixed bonuses (a numeric ability key) AND choosable bonuses (each `choose.from`
/// entry). Reads the raw JsonElement (the typed DeserialiseFields throws on drift); case-tolerant on
/// the `ability` key; ValueKind-guarded; never throws. Empty when there is no ability data.
/// </summary>
public static class RaceAbilityParser
{
    private static readonly HashSet<string> AbilityCodes = new(StringComparer.OrdinalIgnoreCase)
        { "str", "dex", "con", "int", "wis", "cha" };

    public static IReadOnlySet<string> BoostedAbilities(JsonElement fields)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (fields.ValueKind != JsonValueKind.Object) return result;

        // Case-tolerant lookup of the `ability` array (TryGetProperty is case-sensitive).
        var ability = default(JsonElement);
        var found = false;
        foreach (var prop in fields.EnumerateObject())
            if (string.Equals(prop.Name, "ability", StringComparison.OrdinalIgnoreCase))
            {
                ability = prop.Value;
                found = true;
                break;
            }
        if (!found || ability.ValueKind != JsonValueKind.Array) return result;

        foreach (var entry in ability.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            foreach (var prop in entry.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Number && AbilityCodes.Contains(prop.Name))
                {
                    result.Add(prop.Name.ToLowerInvariant());
                }
                else if (string.Equals(prop.Name, "choose", StringComparison.OrdinalIgnoreCase)
                         && prop.Value.ValueKind == JsonValueKind.Object
                         && prop.Value.TryGetProperty("from", out var from)
                         && from.ValueKind == JsonValueKind.Array)
                {
                    foreach (var f in from.EnumerateArray())
                        if (f.ValueKind == JsonValueKind.String && f.GetString() is { } code && AbilityCodes.Contains(code))
                            result.Add(code.ToLowerInvariant());
                }
            }
        }
        return result;
    }
}
```

- [ ] **Step 4: Run → PASS.** Build 0/0. Format.
- [ ] **Step 5: Commit:** `feat(retrieval): RaceAbilityParser (fixed + choosable ability bonuses)`

---

## Task 2: `AbilityBonus` query param + service filter

**Files:**
- Modify: `Features/Retrieval/Entities/EntitySearchQuery.cs` (trailing `AbilityBonus` param)
- Modify: `Features/Retrieval/Entities/EntityRetrievalService.cs` (`RaceAbilityMaxScan` + `ListRacesByAbilityAsync` + the branch)
- Test: `DndMcpAICsharpFun.Tests/Retrieval/` (fake-store service test — find the existing service test that fakes `IEntityVectorStore`)

**Interfaces:**
- `EntitySearchQuery` gains `string? AbilityBonus = null` (trailing). Consumed only by the new branch.

- [ ] **Step 1: Write a failing service test** — find the existing `EntityRetrievalService` unit test + its fake `IEntityVectorStore` (via Serena; mirror how `castableByClass` is tested). Seed three Race envelopes whose `Fields` carry `{"ability":[{"str":2}]}`, `{"ability":[{"choose":{"from":["str","dex"]}}]}`, and `{"ability":[{"dex":2}]}`. Assert: `AbilityBonus="str"` → the first two returned, `total==2`, the dex-only race absent; `AbilityBonus="STR"` behaves identically; `AbilityBonus=null` → the generic path (unchanged). If no fake-store service test exists, add one modeled on the store interface. RED-first.

- [ ] **Step 2: Run → FAIL** (param/method missing).

- [ ] **Step 3: Implement.**
  - Serena-add the trailing param to `EntitySearchQuery`:
    ```csharp
        string? CastableByClass = null,
        // race-ability-filter: restrict to races that boost this ability (fixed or choosable).
        string? AbilityBonus = null);
    ```
  - In `EntityRetrievalService`, add the branch in the list method right after the `CastableByClass` branch, and the helper (mirror `ListSpellsByClassAsync`):
    ```csharp
        private const int RaceAbilityMaxScan = 3000;

        private async Task<EntitySetResult> ListRacesByAbilityAsync(EntitySearchQuery q, int cap, CancellationToken ct)
        {
            var code = q.AbilityBonus!.Trim().ToLowerInvariant();
            var raceFilters = BuildFilters(q) with { Type = EntityType.Race };
            var (_, hits) = await store.ListByFilterAsync(raceFilters, RaceAbilityMaxScan, ct);
            var matched = hits
                .Where(h => RaceAbilityParser.BoostedAbilities(h.Envelope.Fields).Contains(code))
                .ToList();
            var rows = matched.Take(cap).Select(h => ToRow(h.Envelope)).ToList();
            return new EntitySetResult(matched.Count, rows.Count, rows);
        }
    ```
    Branch line (before the generic `ListByFilterAsync`): `if (!string.IsNullOrWhiteSpace(q.AbilityBonus)) return await ListRacesByAbilityAsync(q, clamped, ct);`
  - **VERIFY** `h.Envelope.Fields` is populated on `ListByFilterAsync` hits (the scroll reconstructs the envelope from the Qdrant payload). Read `QdrantEntityVectorStoreScrollTests` / the payload mapper to confirm `Fields` round-trips. If the list path strips `Fields`, flag it — the Task-4 integration test is the definitive check.
  - Do NOT touch `BuildFilters`.

- [ ] **Step 4: Run → PASS.** Build 0/0. Format.
- [ ] **Step 5: Commit:** `feat(retrieval): abilityBonus query-time filter (races by boosted ability)`

---

## Task 3: Endpoint + contract

**Files:**
- Modify: `Features/Retrieval/Entities/EntityRetrievalEndpoints.cs` (`ListPublic` param + `with { AbilityBonus = ... }`)
- Modify: `DndMcpAICsharpFun.http` + `dnd-mcp-api.insomnia.json`

- [ ] **Step 1: Thread the param.** Serena-add `string? abilityBonus` to `ListPublic`'s parameter list (alongside `string? castableByClass`) and extend the `with { CastableByClass = castableByClass }` to `with { CastableByClass = castableByClass, AbilityBonus = abilityBonus }`. Public `/retrieval/entities/list` only. (Minimal-API binds the query string by name → `?abilityBonus=str`.)

- [ ] **Step 2: Update `.http`** — add near the other entities/list examples (~line 275):
```
### List races that boost Strength (race-ability-filter; fixed or choosable)
GET {{baseUrl}}/retrieval/entities/list?abilityBonus=str&limit=50
```
- [ ] **Step 3: Update `dnd-mcp-api.insomnia.json`** — add the mirroring request (same URL/method) in the retrieval group, matching the existing entities/list entries' shape.

- [ ] **Step 4:** Build 0/0; the endpoint-touching tests (if any) green. Format.
- [ ] **Step 5: Commit:** `feat(retrieval): expose abilityBonus on /retrieval/entities/list (+ .http/insomnia)`

---

## Task 4: Real-Qdrant grounding

**Files:**
- Test: `DndMcpAICsharpFun.Tests/VectorStore/Entities/RaceAbilityFilterIntegrationTests.cs` (reuse `QdrantFixture`)

- [ ] **Step 1: Write the integration test.** Mirror `QdrantEntityVectorStoreScrollTests`' fixture/seed pattern. Upsert three Race entities whose `Fields` carry the REAL 5etools ability shapes: fixed `{"ability":[{"str":2}]}`, choose `{"ability":[{"choose":{"from":["str","dex"]}}]}`, and `{"ability":[{"dex":2}]}`. Run the real `EntityRetrievalService.ListAsync` (real Qdrant store) with `AbilityBonus="str"`; assert exactly the fixed-STR + choose-STR races are returned, `total==2`, dex-only absent. This proves (a) `Fields` round-trips through Qdrant onto the list hit, and (b) the parse+filter works end-to-end on real infra.

- [ ] **Step 2: Run → PASS** (needs Docker for Qdrant). Build 0/0.
- [ ] **Step 3: Commit:** `test(retrieval): real-Qdrant race-ability-filter grounding`

---

## Task 5: Gates
- [ ] **Step 1:** `dotnet build` 0/0; FULL `dotnet test` green (Docker for the Qdrant test; if down, run `--filter "FullyQualifiedName!~Qdrant&FullyQualifiedName!~Persistence"` and NOTE the integration test wasn't run — but it is the grounding proof, run it when Docker is up); `dotnet format DndMcpAICsharpFun.slnx --include` the touched files; `git diff --stat` confined to `Features/Retrieval/Entities/*` + `.http` + insomnia + tests; `.http`/insomnia updated; no re-index/migration.

---

## Self-Review notes
- Spec Req 1 (parser: fixed + choosable + empty + no-throw) → Task 1 (5 unit tests incl. case-tolerance + malformed). Req 2 (filter, honest total, case-insensitive, unchanged-when-absent) → Task 2 (fake-store) + Task 4 (real Qdrant) + Task 3 (endpoint).
- Mirrors `castableByClass` exactly (query-time, `Type=Race`, capped scan, `total=matched.Count`); no payload index, no re-index.
- The one real risk — `h.Envelope.Fields` populated on list hits — is verified by reading the scroll/payload path (Task 2) AND proven end-to-end by the real-Qdrant test (Task 4).
- HTTP contract change → `.http` + insomnia in Task 3 (the same-commit rule).
