# Entity Grounding Cascade Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the missing Tier 1 (embedding) + Tier 2 (qwen3 judge) grounding tiers as one shared `GroundingCascade`, use it at extraction time, and add a per-book backlog re-grounding admin pass that auto-promotes grounded entities and flags judge-confirmed fabrications.

**Architecture:** A pure verdict combiner over three injected tiers (Tier 0 = existing OCR field-match; Tier 1 = `dnd_blocks` embedding scoped to the entity's own book + page window, escalation-gate-only; Tier 2 = opt-in qwen3 judge on field support). A verdict→action policy (promote / set `Ungrounded` / no-op, name-gated) drives both extraction disposition and a checkpointed per-book `RegroundService` behind `POST /admin/books/{id}/reground-entities`.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, Qdrant (gRPC) for `dnd_blocks`/`dnd_entities`, Ollama qwen3:8b, xUnit + FluentAssertions, Testcontainers (Postgres + Qdrant).

## Global Constraints

- `net10.0`; nullable; **warnings-as-errors** every project (`Directory.Build.props`).
- Central Package Management — version-less `PackageReference`; versions in `Directory.Packages.props`.
- Use **Serena** symbolic tools for all `.cs` reads/edits; every subagent prompt includes the CRITICAL-Serena block + `initial_instructions`.
- Run `dotnet` with `dangerouslyDisableSandbox: true` (git-crypted config). Solution is `.slnx`.
- Dedup/grounding logic must be table-testable: the verdict combiner and the action policy are PURE (no I/O); tiers are injected and faked in tests.
- **Tier 1 is an escalation gate only — topical embedding similarity SHALL NEVER return `Grounded`.**
- Canonical JSON under `books/canonical/` is edited in place, **never deleted** by automation.
- Endpoint change → update `DndMcpAICsharpFun.http` AND `dnd-mcp-api.insomnia.json` in the same commit.
- New admin route lives under `/admin` (guarded by `AdminApiKeyMiddleware`), added to `BooksAdminEndpoints` with `.DisableAntiforgery()` like its siblings.
- This is retrieval/extraction infra — the Finish step does NOT auto-run `ingest-entities` (dev-flow: extraction/canonical-only).

---

### Task 1: GroundingVerdict + Ungrounded disposition + pure combiner

**Files:**
- Create: `Features/Ingestion/EntityExtraction/GroundingVerdict.cs`
- Modify: `Domain/Entities/EntityDisposition.cs` (add `Ungrounded`)
- Create: `Features/Ingestion/EntityExtraction/GroundingCombiner.cs`
- Test: `DndMcpAICsharpFun.Tests/Ingestion/EntityExtraction/GroundingCombinerTests.cs`

**Interfaces:**
- Produces: `enum GroundingStatus { Grounded, Ungrounded, Uncertain }`; `readonly record struct GroundingVerdict(GroundingStatus Status, int DecidedByTier, double Score)`.
- `enum Tier1Outcome { Grounded /*unused-reserved*/, BelowFloor, AboveFloorEscalate }` — actually model Tier 1 as `record struct Tier1Result(bool BelowFloor, double Score)`.
- `static class GroundingCombiner` with `GroundingVerdict Combine(bool tier0Grounded, Tier1Result? tier1, bool judgeEnabled, bool? tier2Grounded)`.
- `EntityDisposition.Ungrounded = 4`.

- [ ] **Step 1: Add `EntityDisposition.Ungrounded`**

In `Domain/Entities/EntityDisposition.cs`, after `Failed = 3,` add:

```csharp
    /// <summary>A Tier-2-judge-confirmed ungrounded fabrication: the model emitted an entity but
    /// its fields are not supported by the source prose. Excluded from <c>dnd_entities</c>,
    /// retained in canonical for audit. Distinct from a model-chosen <c>Declined</c>.</summary>
    Ungrounded = 4,
```

- [ ] **Step 2: Write the failing combiner tests**

```csharp
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Ingestion.EntityExtraction;

public sealed class GroundingCombinerTests
{
    [Fact]
    public void Tier0_confirm_is_grounded_tier0()
    {
        var v = GroundingCombiner.Combine(tier0Grounded: true, tier1: null, judgeEnabled: false, tier2Grounded: null);
        v.Status.Should().Be(GroundingStatus.Grounded);
        v.DecidedByTier.Should().Be(0);
    }

    [Fact]
    public void Tier1_below_floor_with_judge_is_ungrounded_tier1()
    {
        var v = GroundingCombiner.Combine(false, new Tier1Result(BelowFloor: true, Score: 0.1), judgeEnabled: true, tier2Grounded: null);
        v.Status.Should().Be(GroundingStatus.Ungrounded);
        v.DecidedByTier.Should().Be(1);
    }

    [Fact]
    public void Tier1_below_floor_without_judge_is_uncertain()
    {
        var v = GroundingCombiner.Combine(false, new Tier1Result(true, 0.1), judgeEnabled: false, tier2Grounded: null);
        v.Status.Should().Be(GroundingStatus.Uncertain);
    }

    [Fact]
    public void Tier1_above_floor_escalates_to_tier2_grounded()
    {
        var v = GroundingCombiner.Combine(false, new Tier1Result(BelowFloor: false, Score: 0.8), judgeEnabled: true, tier2Grounded: true);
        v.Status.Should().Be(GroundingStatus.Grounded);
        v.DecidedByTier.Should().Be(2);
    }

    [Fact]
    public void Tier1_above_floor_escalates_to_tier2_ungrounded()
    {
        var v = GroundingCombiner.Combine(false, new Tier1Result(false, 0.8), judgeEnabled: true, tier2Grounded: false);
        v.Status.Should().Be(GroundingStatus.Ungrounded);
        v.DecidedByTier.Should().Be(2);
    }

    [Fact]
    public void Tier1_above_floor_no_judge_is_uncertain()
    {
        var v = GroundingCombiner.Combine(false, new Tier1Result(false, 0.8), judgeEnabled: false, tier2Grounded: null);
        v.Status.Should().Be(GroundingStatus.Uncertain);
    }

    [Fact]
    public void Tier1_never_grounds_on_its_own()
    {
        // above floor, judge enabled but tier2 not yet decided -> must NOT be Grounded by tier1
        var v = GroundingCombiner.Combine(false, new Tier1Result(false, 0.99), judgeEnabled: true, tier2Grounded: null);
        v.Status.Should().NotBe(GroundingStatus.Grounded);
    }
}
```

- [ ] **Step 3: Run tests, verify they fail**

Run: `dotnet test --filter FullyQualifiedName~GroundingCombinerTests` (sandbox disabled). Expected: FAIL (types missing).

- [ ] **Step 4: Implement the types + combiner**

`GroundingVerdict.cs`:

```csharp
namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public enum GroundingStatus { Grounded, Ungrounded, Uncertain }

public readonly record struct GroundingVerdict(GroundingStatus Status, int DecidedByTier, double Score);

public readonly record struct Tier1Result(bool BelowFloor, double Score);
```

`GroundingCombiner.cs`:

```csharp
namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>Pure combination of the three grounding tiers into a verdict. No I/O.</summary>
public static class GroundingCombiner
{
    public static GroundingVerdict Combine(
        bool tier0Grounded, Tier1Result? tier1, bool judgeEnabled, bool? tier2Grounded)
    {
        if (tier0Grounded) return new(GroundingStatus.Grounded, 0, 1.0);
        if (tier1 is not { } t1) return new(GroundingStatus.Uncertain, 1, 0.0);

        // Tier 2 decided (only reachable when judge ran).
        if (tier2Grounded is { } judged)
            return new(judged ? GroundingStatus.Grounded : GroundingStatus.Ungrounded, 2, t1.Score);

        // No judge verdict. Tier 1 alone: reject only if below floor AND judge enabled; else uncertain.
        if (t1.BelowFloor && judgeEnabled)
            return new(GroundingStatus.Ungrounded, 1, t1.Score);

        return new(GroundingStatus.Uncertain, 1, t1.Score);
    }
}
```

- [ ] **Step 5: Run tests, verify pass; build 0/0**

Run: `dotnet test --filter FullyQualifiedName~GroundingCombinerTests` then `dotnet build`. Expected: PASS; 0/0.

- [ ] **Step 6: Commit**

```bash
git add Domain/Entities/EntityDisposition.cs Features/Ingestion/EntityExtraction/GroundingVerdict.cs Features/Ingestion/EntityExtraction/GroundingCombiner.cs DndMcpAICsharpFun.Tests/Ingestion/EntityExtraction/GroundingCombinerTests.cs
git commit -m "feat(grounding): GroundingVerdict + Ungrounded disposition + pure tier combiner"
```

---

### Task 2: Tier 1 embedding grounder (scoped to own book + page window)

**Files:**
- Create: `Features/Ingestion/EntityExtraction/Tier1EmbeddingGrounding.cs`
- Create: `Features/Ingestion/EntityExtraction/GroundingOptions.cs`
- Modify: `Extensions/ServiceCollectionExtensions.cs` (register options + Tier 1)
- Test: `DndMcpAICsharpFun.Tests/Ingestion/EntityExtraction/Tier1EmbeddingGroundingTests.cs`

**Interfaces:**
- Consumes: `IEmbeddingService` (embed the entity text), `IQdrantSearchClient` (`SearchAsync(collection, vector, filter, limit, scoreThreshold, ct)` — `Infrastructure/Qdrant/IQdrantSearchClient.cs`), `QdrantOptions.BlocksCollectionName`, `dnd_blocks` payload fields for `SourceBook` + page (`ChunkMetadata` has `SourceBook`, `PageNumber`, `PageEnd`; confirm the exact Qdrant payload field keys via `QdrantPayloadMapper`/`QdrantPayloadFields` with Serena).
- Produces: `sealed class Tier1EmbeddingGrounding(IEmbeddingService embeddings, IQdrantSearchClient qdrant, IOptions<QdrantOptions> q, IOptions<GroundingOptions> g)` with `Task<Tier1Result> GroundAsync(string entityText, string sourceBook, int? page, CancellationToken ct)`. Builds a Qdrant filter: `SourceBook == sourceBook` AND page within `[page-PageWindow, page+PageWindow]` (skip the page clause when `page` is null); embeds `entityText`; top hit similarity `s` → `new Tier1Result(BelowFloor: s < g.SimilarityFloor, Score: s)`.
- `GroundingOptions { double SimilarityFloor = 0.5; int PageWindow = 2; }` bound from config section `Grounding`.

- [ ] **Step 1: Write failing tests with a fake `IQdrantSearchClient` + fake `IEmbeddingService`**

Model on existing retrieval unit tests (find one that fakes `IQdrantSearchClient` with Serena). Assert:
- Fake returns a top score below `SimilarityFloor` → `Tier1Result.BelowFloor == true`, `Score` echoed.
- Fake returns a top score at/above floor → `BelowFloor == false`.
- The `filter` passed to `SearchAsync` restricts to the given `sourceBook` and page window (inspect the captured filter in the fake — assert the field/range conditions).
- `page == null` → no page clause, book-only filter.

Write the test code fully; capture the filter via a recording fake.

- [ ] **Step 2: Run, verify fail**

Run: `dotnet test --filter FullyQualifiedName~Tier1EmbeddingGroundingTests`. Expected: FAIL.

- [ ] **Step 3: Implement `GroundingOptions` + `Tier1EmbeddingGrounding`**

Confirm via Serena the exact `dnd_blocks` payload field constants for book + page (used by `RagRetrievalService`/`QdrantPayloadMapper` when filtering blocks — e.g. `QdrantPayloadFields.SourceBook`, a page field). Build the filter with the same `KeywordCondition`/range helpers those services use. Embed via `IEmbeddingService.EmbedAsync([entityText])`. Query with `limit: 1` (or a small pool, take max score).

- [ ] **Step 4: Register in DI**

In `Extensions/ServiceCollectionExtensions.cs`: `services.Configure<GroundingOptions>(configuration.GetSection("Grounding"));` and `services.AddScoped<Tier1EmbeddingGrounding>();` (near the extraction/retrieval registrations).

- [ ] **Step 5: Run tests pass; build 0/0. Commit**

```bash
git add Features/Ingestion/EntityExtraction/Tier1EmbeddingGrounding.cs Features/Ingestion/EntityExtraction/GroundingOptions.cs Extensions/ServiceCollectionExtensions.cs DndMcpAICsharpFun.Tests/Ingestion/EntityExtraction/Tier1EmbeddingGroundingTests.cs
git commit -m "feat(grounding): Tier 1 embedding grounder scoped to own book + page window"
```

---

### Task 3: Tier 2 judge behind an interface

**Files:**
- Create: `Features/Ingestion/EntityExtraction/IGroundingJudge.cs`
- Create: `Features/Ingestion/EntityExtraction/QwenGroundingJudge.cs`
- Modify: `Extensions/ServiceCollectionExtensions.cs`
- Test: `DndMcpAICsharpFun.Tests/Ingestion/EntityExtraction/QwenGroundingJudgePromptTests.cs` (prompt-shape unit test with a fake Ollama transport)

**Interfaces:**
- Produces: `interface IGroundingJudge { Task<bool> AreFieldsSupportedAsync(EntityEnvelope entity, string sourceProse, CancellationToken ct); }`.
- `sealed class QwenGroundingJudge(...)` reusing the Ollama client that `OllamaEntityExtractionClient` (`Features/Ingestion/EntityExtraction/OllamaEntityExtractionClient.cs`) depends on — inspect its constructor with Serena and inject the same underlying client/options. Prompt: give the entity's emitted fields (JSON) + `sourceProse`; ask strictly "are these fields supported by this prose? answer yes/no". Parse yes→true.

- [ ] **Step 1: Failing test** — with a fake transport returning a canned "yes"/"no", assert `AreFieldsSupportedAsync` maps to true/false and that the prompt includes the entity fields + the source prose (assert on the captured request text). (If the Ollama client isn't cleanly fakeable, extract a minimal seam interface for the single call the judge needs and fake that — note it in the report.)
- [ ] **Step 2: Run, verify fail.**
- [ ] **Step 3: Implement `IGroundingJudge` + `QwenGroundingJudge`.** Register `services.AddScoped<IGroundingJudge, QwenGroundingJudge>();`.
- [ ] **Step 4: Tests pass; build 0/0. Commit**

```bash
git commit -am "feat(grounding): Tier 2 qwen3 field-support judge behind IGroundingJudge"
```

---

### Task 4: GroundingCascade (compose the tiers)

**Files:**
- Create: `Features/Ingestion/EntityExtraction/GroundingCascade.cs`
- Modify: `Extensions/ServiceCollectionExtensions.cs`
- Test: `DndMcpAICsharpFun.Tests/Ingestion/EntityExtraction/GroundingCascadeTests.cs`

**Interfaces:**
- Consumes: `Tier0FieldGrounding` (static), `Tier1EmbeddingGrounding`, `IGroundingJudge`, `GroundingCombiner`.
- Produces: `sealed class GroundingCascade(Tier1EmbeddingGrounding tier1, IGroundingJudge judge)` with `Task<GroundingVerdict> GradeAsync(EntityEnvelope entity, string sourceProse, bool judgeEnabled, CancellationToken ct)`.
  - Tier 0: `HasAnyFieldGrounded(entity.Fields, sourceProse)` (reuse the `Tier0FieldGrounding.IsTextGrounded` loop currently inlined in `EntityExtractionRunner.HasGroundedContent` — extract it to a shared helper on `Tier0FieldGrounding` so both call it). If true → `Combine(true, null, ...)`, return (no Tier 1/2).
  - Tier 1: `await tier1.GroundAsync(entity.CanonicalText-or-rendered, entity.SourceBook, entity.Page, ct)`.
  - If Tier 1 above floor AND `judgeEnabled` → `await judge.AreFieldsSupportedAsync(...)`; else no judge call.
  - `return GroundingCombiner.Combine(false, t1, judgeEnabled, tier2Grounded)`.

- [ ] **Step 1: Failing tests** with fake Tier 1 (inject via a small `ITier1Grounding` seam) + fake `IGroundingJudge`:
  - Tier 0 field present → verdict Grounded(0), tier1/judge NOT called.
  - Tier 1 below floor, judge enabled → Ungrounded(1), judge NOT called.
  - Tier 1 above floor, judge enabled → judge called once; judge yes→Grounded(2), no→Ungrounded(2).
  - Tier 1 above floor, judge disabled → Uncertain, judge NOT called.
  (To fake Tier 1, extract `interface ITier1Grounding { Task<Tier1Result> GroundAsync(...) }` implemented by `Tier1EmbeddingGrounding`; the cascade depends on the interface.)
- [ ] **Step 2: Run, verify fail.**
- [ ] **Step 3: Implement `GroundingCascade` + `ITier1Grounding` seam; extract the shared Tier 0 helper. Register `AddScoped<GroundingCascade>()`.**
- [ ] **Step 4: Tests pass; build 0/0. Commit**

```bash
git commit -am "feat(grounding): GroundingCascade composes Tier 0/1/2 with short-circuits"
```

---

### Task 5: Verdict → action policy (name-gated)

**Files:**
- Create: `Features/Ingestion/EntityExtraction/GroundingActionPolicy.cs`
- Test: `DndMcpAICsharpFun.Tests/Ingestion/EntityExtraction/GroundingActionPolicyTests.cs`

**Interfaces:**
- Produces: `enum GroundingAction { Promote, MarkUngrounded, LeaveFlagged }` and `static class GroundingActionPolicy` with `GroundingAction Decide(GroundingVerdict verdict, string name)`.
  - `Grounded` → `HasOcrArtifacts(name) ? LeaveFlagged : Promote`.
  - `Ungrounded` → `MarkUngrounded`.
  - `Uncertain` → `LeaveFlagged`.
- Consumes: `ExtractionNeedsReview.HasOcrArtifacts` (`Features/Ingestion/EntityExtraction/ExtractionNeedsReview.cs`).

- [ ] **Step 1: Failing tests** covering all six combinations (each status × clean/garbled name where relevant). Grounded+clean→Promote; Grounded+garbled→LeaveFlagged; Ungrounded→MarkUngrounded; Uncertain→LeaveFlagged.
- [ ] **Step 2: Run fail → implement → pass → build 0/0.**
- [ ] **Step 3: Commit** `feat(grounding): verdict->action policy with name gate`.

---

### Task 6: Extraction-time integration

**Files:**
- Modify: `Features/Ingestion/EntityExtraction/EntityExtractionRunner.cs` (`BuildTypedEnvelope`, `HasGroundedContent`)
- Modify: `Features/Ingestion/EntityExtraction/ExtractionDispositionPolicy.cs` (accept `Ungrounded` verdict)
- Test: `DndMcpAICsharpFun.Tests/Ingestion/EntityExtraction/...` (existing extraction/disposition tests + new)

**Interfaces:**
- `ExtractionDispositionPolicy.Derive` currently `(bool grounded, string name, string? confidence)`. Add an overload/param carrying the `GroundingStatus` so an `Ungrounded` verdict maps to `EntityDisposition.Ungrounded` (not `NeedsReview`): `Derive(GroundingVerdict verdict, string name, string? confidence)` → Grounded path runs the existing name/confidence gate; Ungrounded → `Ungrounded`; Uncertain → `NeedsReview`.
- `BuildTypedEnvelope` gains access to the `GroundingCascade` (inject into `EntityExtractionRunner`) and calls `GradeAsync(envelope-so-far, candidate.Text, judgeEnabled: <run flag>, ct)`. Since Tier 0 uses `candidate.Text` and short-circuits, the common path stays cheap. The run's `judgeEnabled` comes from the extraction options/request (default false to keep extraction cost unchanged).

- [ ] **Step 1: Failing tests** — an ungrounded fabrication (Tier 0 fails, Tier 1 below floor, judge on) → disposition `Ungrounded`; a Tier-0-grounded entity → `Accepted`; an escalated-but-no-judge entity → `NeedsReview`. Preserve existing `HasGroundedContent`-era behavior for the Tier 0 cases (existing tests must stay green).
- [ ] **Step 2: Run fail.**
- [ ] **Step 3: Implement** — inject `GroundingCascade` into `EntityExtractionRunner`; replace `HasGroundedContent` with `GradeAsync`; update `ExtractionDispositionPolicy.Derive` to the verdict overload; thread a `judgeEnabled` flag from extraction options (default false). Fix construction sites (Serena `find_referencing_symbols` on the runner ctor / policy). 
- [ ] **Step 4: Full extraction test namespace green; build 0/0. Commit** `feat(grounding): extraction uses shared cascade; Ungrounded disposition`.

---

### Task 7: RegroundService (backlog pass)

**Files:**
- Create: `Features/Admin/RegroundService.cs`
- Create: `Features/Admin/RegroundResult.cs`
- Modify: `Extensions/ServiceCollectionExtensions.cs`
- Test: `DndMcpAICsharpFun.Tests/Admin/RegroundServiceTests.cs`

**Interfaces:**
- Reuse: `CanonicalJsonLoader` (load) + the canonical writer used by `NeedsReviewService` (`Features/Admin/NeedsReviewService.cs` writes canonical + `IEntityIngestionOrchestrator.ReindexEntityAsync(bookId, entityId, ct)` — read it as the reference and reuse the same write+reindex pattern); `GroundingCascade`; `GroundingActionPolicy`; the entity's source prose (fetch the entity's `dnd_blocks` neighbourhood by book+page, OR reuse the extraction candidate text if available — for the backlog pass, query `dnd_blocks` scoped like Tier 1 to assemble `sourceProse`).
- Checkpoint: mirror `ExtractionCheckpointStore` (`Features/Ingestion/EntityExtraction/ExtractionCheckpointStore.cs`) with a `<slug>.reground.progress.json` sidecar (processed entity ids), written every N, deleted on success, read on resume.
- Produces: `sealed record RegroundResult(int Scanned, int Promoted, int MarkedUngrounded, int StillFlagged, int Tier2Invoked)`; `sealed class RegroundService(...)` with `Task<RegroundResult> RegroundAsync(int bookId, bool judge, CancellationToken ct)`.
  - Load canonical; select entities with `Disposition == NeedsReview` (or legacy `NeedsReview==true`); for each: assemble `sourceProse`, `verdict = cascade.GradeAsync(entity, sourceProse, judge, ct)`, `action = GroundingActionPolicy.Decide(verdict, entity.Name)`.
  - Promote → clear NeedsReview/set `Accepted`; MarkUngrounded → set `Ungrounded`; LeaveFlagged → no-op.
  - On change: write canonical (in place) + `ReindexEntityAsync`. Count `Tier2Invoked` when `verdict.DecidedByTier == 2`.
  - Checkpoint every N processed; delete sidecar on success.

- [ ] **Step 1: Failing tests** with fakes (fake `GroundingCascade` seam or inject fake tiers; fake orchestrator recording reindex calls; a temp canonical file). Assert: a seeded book with 3 NeedsReview entities (one grounds, one judge-ungrounded, one uncertain) → `RegroundResult{Scanned:3,Promoted:1,MarkedUngrounded:1,StillFlagged:1}`; canonical written with the right dispositions; `ReindexEntityAsync` called once per CHANGED entity (2), not for the uncertain one; NO canonical file deleted; checkpoint written then deleted; a resume run with a pre-seeded checkpoint skips completed ids. (Extract an `IGroundingCascade` seam if needed to fake grading deterministically.)
- [ ] **Step 2: Run fail → implement `RegroundService` + `RegroundResult` + DI → pass → build 0/0.**
- [ ] **Step 3: Commit** `feat(grounding): RegroundService per-book backlog pass (checkpointed)`.

---

### Task 8: Admin endpoint + contracts

**Files:**
- Modify: `Features/Admin/BooksAdminEndpoints.cs`
- Test: `DndMcpAICsharpFun.Tests/Admin/RegroundEndpointTests.cs`
- Modify: `DndMcpAICsharpFun.http`, `dnd-mcp-api.insomnia.json`

**Interfaces:**
- Add `group.MapPost("/books/{id:int}/reground-entities", Reground).DisableAntiforgery();` and a handler `Reground(int id, bool judge, RegroundService svc, CancellationToken ct) => Results.Ok(await svc.RegroundAsync(id, judge, ct))`. Match how sibling handlers bind optional bool query params (e.g. `extract-entities?force=`) — mirror it (`bool judge = false`).

- [ ] **Step 1: Failing endpoint tests** (reuse the existing books-admin endpoint test harness — find `BooksAdminEndpointsTests` with Serena): missing admin key → rejected; `POST .../reground-entities` → 200 + `RegroundResult` body with `tier2Invoked==0`; `?judge=true` → 200 and (with a fake judge wired) `tier2Invoked>0`.
- [ ] **Step 2: Run fail → map route + DI → pass.**
- [ ] **Step 3: Update `.http`** near the other `/admin/books/{id}` entries:

```
### Admin Books — Re-ground NeedsReview entities (Tier 0/1 only; fast, no LLM)
POST {{baseUrl}}/admin/books/1/reground-entities
X-Admin-Api-Key: {{adminKey}}

### Admin Books — Re-ground with the Tier 2 qwen3 judge (slow; promotes grounded, flags fabrications)
POST {{baseUrl}}/admin/books/1/reground-entities?judge=true
X-Admin-Api-Key: {{adminKey}}
```

- [ ] **Step 4: Mirror both into `dnd-mcp-api.insomnia.json`** (admin-key header, both variants); validate JSON: `python3 -c "import json;json.load(open('dnd-mcp-api.insomnia.json'));print('valid')"`.
- [ ] **Step 5: build 0/0; ~Admin tests green. Commit** `feat(grounding): reground-entities admin endpoint + .http/insomnia`.

---

### Task 9: Verify + review

- [ ] **Step 1: Full build 0/0 + full suite** (`dotnet test`, Docker up for Testcontainers) green.
- [ ] **Step 2: Live-host drive (per `verify`)** — start the host; on a book with NeedsReview entities run the fast pass then `?judge=true`; confirm promotions/flags in canonical + Qdrant and `git status books/canonical/` shows edits but NO deletions. (Operational — defer honestly if the stack is down; Testcontainers cover the store paths.)
- [ ] **Step 3: Whole-branch opus review** — cross-check every ADDED/MODIFIED requirement (cascade verdict, Tier 1 escalation-only + scoping, Tier 2 field-support, verdict→action + name gate, backlog endpoint, `Ungrounded` disposition). Address findings; then stop for the user's "commit"/"archive" directive.
