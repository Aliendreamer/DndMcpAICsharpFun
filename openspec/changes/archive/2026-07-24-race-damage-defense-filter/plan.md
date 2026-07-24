# Race Damage-Defense Filter — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Filter entity retrieval by a race's damage `resist`/`immune` (fixed AND choosable), query-time like `abilityBonus` — no re-index.

**Architecture:** A pure `RaceDamageDefenseParser.DamageTypes(fields, key)` reads the resist/immune damage-type set from an entity's raw `Fields`. `EntityRetrievalService` gains a `ListRacesByDamageDefenseAsync` mirroring the just-shipped `ListRacesByAbilityAsync`, ANDing resist+immune. `resist`/`immune` params thread through `EntitySearchQuery` + `GET /retrieval/entities/list`. This is a near-copy of the archived `race-ability-filter` (2026-07-24) — read those files for the exact precedent.

**Tech Stack:** C# / .NET 10, xunit + FluentAssertions, real-Qdrant via `QdrantFixture`. Serena for all tracked files.

## Global Constraints
- **Serena only** for tracked files (subagents too; `.superpowers/` report = built-in Write; stop after 1 >2min hang).
- **Work on `main`**; commit per task. Build 0/0. `dotnet` needs `dangerouslyDisableSandbox: true`. Ignore LSP false CS0246 on tests.
- **HTTP contract change** (two new query params) → `.http` AND `dnd-mcp-api.insomnia.json` same commit (Task 3).
- Query-time filter (NOT payload fields): do NOT add `ResistDamage`/`ImmuneDamage` to `BuildFilters`. No re-index, no GPU, no migration.
- Grounding: a race with no resist/immune data never matches; parser never throws. Unset param never constrains; both set AND.
- Scope: resist + immune only (a combined single param + any chat/MCP tool are OUT).

**Known shapes (verified — from race-ability-filter, just shipped):**
```csharp
// EntitySearchQuery (Features/Retrieval/Entities/EntitySearchQuery.cs): trailing `string? CastableByClass = null, string? AbilityBonus = null`.
// EntityRetrievalService: consts SpellClassMaxScan=3000, RaceAbilityMaxScan=3000; list method branches:
//   if (!IsNullOrWhiteSpace(q.CastableByClass)) return await ListSpellsByClassAsync(q, clamped, ct);
//   if (!IsNullOrWhiteSpace(q.AbilityBonus))    return await ListRacesByAbilityAsync(q, clamped, ct);
//   var (total, hits) = await store.ListByFilterAsync(BuildFilters(q), clamped, ct); ...
//   ListRacesByAbilityAsync: `var code = q.AbilityBonus!.Trim().ToLowerInvariant(); var raceFilters = BuildFilters(q) with { Type = EntityType.Race }; var (_, hits) = await store.ListByFilterAsync(raceFilters, RaceAbilityMaxScan, ct); var matched = hits.Where(h => RaceAbilityParser.BoostedAbilities(h.Envelope.Fields).Contains(code)).ToList(); var rows = matched.Take(cap).Select(h => ToRow(h.Envelope)).ToList(); return new EntitySetResult(matched.Count, rows.Count, rows);`
// h.Envelope.Fields is a JsonElement, POPULATED on ListByFilterAsync hits (payloadSelector:true → ToEnvelope parses FieldsJson) — proven by RaceAbilityFilterIntegrationTests.
// RaceAbilityParser (Features/Retrieval/Entities/RaceAbilityParser.cs): the parser precedent — raw JsonElement, case-tolerant key, ValueKind-guarded, never throws. READ IT to mirror structure.
// EntityRetrievalEndpoints.ListPublic(... string? castableByClass, string? abilityBonus, IEntityRetrievalService svc, CancellationToken ct, int limit=50)
//   -> BuildQuery(...) with { CastableByClass = castableByClass, AbilityBonus = abilityBonus }.
// .http: entities/list examples ~lines 275-278 (…?castableByClass=…, …?abilityBonus=str…). insomnia: req added under fld_retrieval by race-ability-filter — mirror its shape.
// Integration precedent: DndMcpAICsharpFun.Tests/VectorStore/Entities/RaceAbilityFilterIntegrationTests.cs — READ + mirror its seed/service-construction pattern.
// 5etools race resist/immune shapes: resist = strings ["necrotic","radiant"] + 6 choose-blocks {"choose":{"from":[…]}}; immune = strings only ["poison"].
```

---

## Task 1: `RaceDamageDefenseParser`

**Files:**
- Create: `Features/Retrieval/Entities/RaceDamageDefenseParser.cs`
- Test: `DndMcpAICsharpFun.Tests/Retrieval/RaceDamageDefenseParserTests.cs`

**Interfaces:**
- Produces: `public static IReadOnlySet<string> RaceDamageDefenseParser.DamageTypes(JsonElement fields, string key)`.

- [ ] **Step 1: Write failing tests** (feed raw `JsonElement`s via `JsonDocument.Parse`). READ `RaceAbilityParserTests` first via Serena for style.
```csharp
using System.Text.Json;
using DndMcpAICsharpFun.Features.Retrieval.Entities;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Retrieval;

public sealed class RaceDamageDefenseParserTests
{
    private static JsonElement Fields(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Direct_strings()
        => RaceDamageDefenseParser.DamageTypes(Fields("""{"resist":["necrotic","radiant"]}"""), "resist")
            .Should().BeEquivalentTo(["necrotic", "radiant"]);

    [Fact]
    public void Choosable_included()
        => RaceDamageDefenseParser.DamageTypes(Fields("""{"resist":[{"choose":{"from":["acid","fire"]}}]}"""), "resist")
            .Should().Contain("acid").And.Contain("fire");

    [Fact]
    public void Mixed_string_and_choose()
        => RaceDamageDefenseParser.DamageTypes(Fields("""{"resist":["cold",{"choose":{"from":["fire"]}}]}"""), "resist")
            .Should().BeEquivalentTo(["cold", "fire"]);

    [Fact]
    public void Reads_the_requested_key_only()
    {
        var f = Fields("""{"immune":["poison"]}""");
        RaceDamageDefenseParser.DamageTypes(f, "immune").Should().BeEquivalentTo(["poison"]);
        RaceDamageDefenseParser.DamageTypes(f, "resist").Should().BeEmpty(); // no resist key
    }

    [Fact]
    public void Case_tolerant_key_and_malformed_ignored_not_thrown()
    {
        RaceDamageDefenseParser.DamageTypes(Fields("""{"Resist":["fire"]}"""), "resist").Should().Contain("fire"); // key case-tolerant
        RaceDamageDefenseParser.DamageTypes(Fields("""{"resist":"nonsense"}"""), "resist").Should().BeEmpty();     // wrong kind → no throw
        RaceDamageDefenseParser.DamageTypes(Fields("""[1,2,3]"""), "resist").Should().BeEmpty();                   // not an object → no throw
    }
}
```

- [ ] **Step 2: Run → FAIL** (type missing). Run: `dotnet test --filter "FullyQualifiedName~RaceDamageDefenseParser"` (dangerouslyDisableSandbox).

- [ ] **Step 3: Implement** `Features/Retrieval/Entities/RaceDamageDefenseParser.cs`:
```csharp
using System.Text.Json;

namespace DndMcpAICsharpFun.Features.Retrieval.Entities;

/// <summary>
/// The set of damage-type codes a race resists / is immune to, from its raw entity <c>Fields</c> —
/// direct string entries AND each <c>choose.from</c> entry, under the given key (<c>resist</c> or
/// <c>immune</c>). Reads the raw JsonElement (the typed DeserialiseFields throws on drift); case-tolerant
/// on the key; ValueKind-guarded; never throws. Empty when there is no data for that key.
/// Sibling of RaceAbilityParser (which reads numeric ability keys; here the entries are damage-type strings).
/// </summary>
public static class RaceDamageDefenseParser
{
    public static IReadOnlySet<string> DamageTypes(JsonElement fields, string key)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (fields.ValueKind != JsonValueKind.Object) return result;

        // Case-tolerant lookup of the key array (TryGetProperty is case-sensitive).
        var arr = default(JsonElement);
        var found = false;
        foreach (var prop in fields.EnumerateObject())
            if (string.Equals(prop.Name, key, StringComparison.OrdinalIgnoreCase))
            {
                arr = prop.Value;
                found = true;
                break;
            }
        if (!found || arr.ValueKind != JsonValueKind.Array) return result;

        foreach (var entry in arr.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.String)
            {
                if (entry.GetString() is { } s && !string.IsNullOrWhiteSpace(s)) result.Add(s.ToLowerInvariant());
            }
            else if (entry.ValueKind == JsonValueKind.Object
                     && entry.TryGetProperty("choose", out var choose)
                     && choose.ValueKind == JsonValueKind.Object
                     && choose.TryGetProperty("from", out var from)
                     && from.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in from.EnumerateArray())
                    if (f.ValueKind == JsonValueKind.String && f.GetString() is { } d && !string.IsNullOrWhiteSpace(d))
                        result.Add(d.ToLowerInvariant());
            }
        }
        return result;
    }
}
```

- [ ] **Step 4: Run → PASS.** Build 0/0. Format.
- [ ] **Step 5: Commit:** `feat(retrieval): RaceDamageDefenseParser (resist/immune damage types, fixed + choosable)`

---

## Task 2: `resist`/`immune` params + service filter

**Files:**
- Modify: `Features/Retrieval/Entities/EntitySearchQuery.cs` (trailing `ResistDamage`, `ImmuneDamage`)
- Modify: `Features/Retrieval/Entities/EntityRetrievalService.cs` (`RaceDamageDefenseMaxScan` + `ListRacesByDamageDefenseAsync` + the branch)
- Test: the same fake-store service test file that covers `AbilityBonus` (find it via Serena — it's the one with the fake `IEntityVectorStore` where `ListRacesByAbilityAsync` is tested)

- [ ] **Step 1: Write a failing service test** (mirror the `AbilityBonus` fake-store test). Seed four Race envelopes with `Fields` = `{"resist":["fire"]}`, `{"resist":[{"choose":{"from":["fire","cold"]}}]}`, `{"immune":["poison"]}`, `{}` (plain). Assert: `ResistDamage="fire"` → the two fire races, `total==2`; `ImmuneDamage="poison"` → the one immune race; `ResistDamage="fire"` + `ImmuneDamage="poison"` → none; `ResistDamage="FIRE"` == `"fire"`; neither set → generic path unchanged. RED-first.

- [ ] **Step 2: Run → FAIL** (params/method missing).

- [ ] **Step 3: Implement.**
  - Serena-add the trailing params to `EntitySearchQuery` (after `AbilityBonus`):
    ```csharp
        string? AbilityBonus = null,
        // race-damage-defense-filter: restrict to races that resist / are immune to a damage type.
        string? ResistDamage = null,
        string? ImmuneDamage = null);
    ```
  - In `EntityRetrievalService`, add the branch right after the `AbilityBonus` branch, and the helper:
    ```csharp
        private const int RaceDamageDefenseMaxScan = 3000;

        private async Task<EntitySetResult> ListRacesByDamageDefenseAsync(EntitySearchQuery q, int cap, CancellationToken ct)
        {
            var resist = string.IsNullOrWhiteSpace(q.ResistDamage) ? null : q.ResistDamage!.Trim().ToLowerInvariant();
            var immune = string.IsNullOrWhiteSpace(q.ImmuneDamage) ? null : q.ImmuneDamage!.Trim().ToLowerInvariant();
            var raceFilters = BuildFilters(q) with { Type = EntityType.Race };
            var (_, hits) = await store.ListByFilterAsync(raceFilters, RaceDamageDefenseMaxScan, ct);
            var matched = hits
                .Where(h =>
                    (resist is null || RaceDamageDefenseParser.DamageTypes(h.Envelope.Fields, "resist").Contains(resist))
                    && (immune is null || RaceDamageDefenseParser.DamageTypes(h.Envelope.Fields, "immune").Contains(immune)))
                .ToList();
            var rows = matched.Take(cap).Select(h => ToRow(h.Envelope)).ToList();
            return new EntitySetResult(matched.Count, rows.Count, rows);
        }
    ```
    Branch line (after the `AbilityBonus` branch, before the generic `ListByFilterAsync`):
    ```csharp
        if (!string.IsNullOrWhiteSpace(q.ResistDamage) || !string.IsNullOrWhiteSpace(q.ImmuneDamage))
            return await ListRacesByDamageDefenseAsync(q, clamped, ct);
    ```
  - Do NOT touch `BuildFilters`.

- [ ] **Step 4: Run → PASS.** Build 0/0. Format.
- [ ] **Step 5: Commit:** `feat(retrieval): resist/immune query-time filters (races by damage defense)`

---

## Task 3: Endpoint + contract

**Files:**
- Modify: `Features/Retrieval/Entities/EntityRetrievalEndpoints.cs`
- Modify: `DndMcpAICsharpFun.http` + `dnd-mcp-api.insomnia.json`

- [ ] **Step 1: Thread the params.** Serena-add `string? resist, string? immune` to `ListPublic`'s parameter list (after `string? abilityBonus`) and extend `with { CastableByClass = castableByClass, AbilityBonus = abilityBonus }` to `with { CastableByClass = castableByClass, AbilityBonus = abilityBonus, ResistDamage = resist, ImmuneDamage = immune }`. Public `/retrieval/entities/list` only.

- [ ] **Step 2: Update `.http`** — near the abilityBonus example (~line 278):
```
### List races resistant to fire (race-damage-defense-filter; fixed or choosable)
GET {{baseUrl}}/retrieval/entities/list?resist=fire&limit=50

### List races immune to poison
GET {{baseUrl}}/retrieval/entities/list?immune=poison&limit=50
```
- [ ] **Step 3: Update `dnd-mcp-api.insomnia.json`** — add two mirroring requests (resist, immune) under the same retrieval group, matching the shape of the `abilityBonus` request that race-ability-filter added (read it first). Validate: `python3 -c "import json;json.load(open('dnd-mcp-api.insomnia.json'));print('ok')"`.

- [ ] **Step 4:** Build 0/0; touched tests green. Format.
- [ ] **Step 5: Commit:** `feat(retrieval): expose resist/immune on /retrieval/entities/list (+ .http/insomnia)`

---

## Task 4: Real-Qdrant grounding

**Files:**
- Test: `DndMcpAICsharpFun.Tests/VectorStore/Entities/RaceDamageDefenseFilterIntegrationTests.cs` (reuse `QdrantFixture`; mirror `RaceAbilityFilterIntegrationTests`)

- [ ] **Step 1: Write the integration test.** READ `RaceAbilityFilterIntegrationTests` via Serena and mirror its seed/service-construction pattern. Upsert Race entities with the REAL shapes: `{"resist":["fire"]}`, `{"resist":[{"choose":{"from":["fire","cold"]}}]}`, `{"immune":["poison"]}`, and a plain race. Run the real `EntityRetrievalService.ListAsync` with `ResistDamage="fire"` → assert exactly the two fire races (`total==2`), plain absent; with `ImmuneDamage="poison"` → the one immune race. Proves `Fields` round-trips + parse/filter end-to-end on real Qdrant.

- [ ] **Step 2: Run → PASS** (needs Docker for Qdrant). Build 0/0.
- [ ] **Step 3: Commit:** `test(retrieval): real-Qdrant race-damage-defense grounding`

---

## Task 5: Gates
- [ ] **Step 1:** `dotnet build` 0/0; FULL `dotnet test` green (Docker for the Qdrant test; if down, run `--filter "FullyQualifiedName!~Qdrant&FullyQualifiedName!~Persistence"` and NOTE the integration test wasn't run — it is the grounding proof, run when Docker is up); `dotnet format DndMcpAICsharpFun.slnx --include` touched files; `git diff --stat` confined to `Features/Retrieval/Entities/*` + `.http` + insomnia + tests; `.http`/insomnia updated; no re-index/migration.

---

## Self-Review notes
- Spec Req 1 (parser: strings + choose + key-scoped + empty/no-throw + case-tolerant) → Task 1 (5 units). Req 2 (resist/immune filter, AND when both, honest total, case-insensitive, unchanged-when-absent) → Task 2 (fake-store) + Task 4 (real Qdrant) + Task 3 (endpoint).
- Mirrors the archived `race-ability-filter` exactly (query-time, `Type=Race`, capped scan, `total=matched.Count`); no payload index, no re-index. The one new element is the AND of two params (unset = no-op).
- `h.Envelope.Fields` populated on list hits — already proven by `race-ability-filter`; re-proven end-to-end by Task 4.
- HTTP contract change → `.http` + insomnia in Task 3.
