# Rules Adjudication v2 (Multi-hop) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** `ask_rules` gains optional `ruleTopics` multi-hop retrieval — the LLM names the distinct rules a question involves and each is retrieved separately (scoped to the core rulebooks), so every interacting rule is grounded regardless of ranking. Single-shot stays the default.

**Architecture:** Additive over shipped v1. `RulesAdjudicationService.AskAsync` gains a `ruleTopics` param → per-topic scoped retrieval + merge (deduped flat `Passages` + per-topic `Topics` groups). Reuses the shipped `RetrievalQuery.SourceBooks` filter. No new LLM call, no migration, no UI.

**Tech Stack:** .NET 10, C#, xUnit + FluentAssertions + NSubstitute, Testcontainers (Qdrant), Microsoft.Extensions.AI chat tools.

## Global Constraints

- net10.0; nullable; **warnings-as-errors** — no new warnings; no new package.
- `ask_rules` stays ownership-free (no `userId`/`campaignId`; `ruleTopics` is a plain `string[]?`). No HTTP route, no `.http`/`.insomnia`, no migration, no shared-key MCP surface.
- **Re-run the FULL suite after the value/shape change (dev-flow: a result-shape/signature change can break sibling tests) — not a feature-filtered subset.**
- `dotnet` fails under the command sandbox (git-crypted `Config/`) — run every `dotnet` with `dangerouslyDisableSandbox: true`, timeout ~300000ms (~400000ms for container integration tests). LSP shows stale/false CS errors on changed test files — trust `dotnet build`/`dotnet test`.
- Serena symbolic tools for all code reads/edits (prefer literal/symbol edits — regex-mode can corrupt literal `\n`). Work on `main`; commit each reviewed task.

---

### Task 1: Service multi-hop + additive result shape

**Files:**
- Modify: `Features/Rules/RuleSources.cs` (add `TopicTopK`), `Features/Rules/RulesRulingResult.cs` (add `Topics` + `RuleTopicPassages`), `Features/Rules/RulesAdjudicationService.cs` (`ruleTopics` param + per-topic retrieval)
- Modify (v1 test signature): `DndMcpAICsharpFun.Tests/Rules/RulesAdjudicationServiceTests.cs`

**Interfaces:**
- Produces: `RuleSources.TopicTopK` (5); `RuleTopicPassages(string Topic, IReadOnlyList<CitedPassage> Passages)`; `RulesRulingResult(Passages, ScopedBooks, IReadOnlyList<RuleTopicPassages> Topics)`; `RulesAdjudicationService.AskAsync(string question, IReadOnlyList<string>? ruleTopics, DndVersion? edition, CancellationToken ct)`.

- [ ] **Step 1: Write the failing tests (add multi-hop cases; update v1 calls to the new signature)**

Update the three existing tests' `AskAsync(...)` calls to pass `ruleTopics: null` (behavior-preserving), then add:

```csharp
[Fact]
public async Task Multihop_runs_one_scoped_retrieval_per_topic_and_groups_them()
{
    var rag = Substitute.For<IRagRetrievalService>();
    rag.SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>())
        .Returns(ci => {
            var q = ci.Arg<RetrievalQuery>();
            // return one passage naming the topic's book, so we can see per-topic grouping
            return (IReadOnlyList<RetrievalResult>)Results((q.QueryText + " rule", "PlayerHandbook 2014"));
        });
    var svc = new RulesAdjudicationService(rag);

    var result = await svc.AskAsync("grapple while prone",
        ruleTopics: ["grappling", "prone condition"], edition: null, CancellationToken.None);

    // one retrieval per topic, each scoped to the rulebooks at TopicTopK
    await rag.Received().SearchAsync(
        Arg.Is<RetrievalQuery>(q => q.QueryText == "grappling"
            && q.SourceBooks!.Contains("PlayerHandbook 2014") && q.TopK == RuleSources.TopicTopK),
        Arg.Any<CancellationToken>());
    await rag.Received().SearchAsync(
        Arg.Is<RetrievalQuery>(q => q.QueryText == "prone condition" && q.TopK == RuleSources.TopicTopK),
        Arg.Any<CancellationToken>());
    result.Topics.Select(t => t.Topic).Should().Equal("grappling", "prone condition");
    result.Topics.Should().OnlyContain(t => t.Passages.Count > 0);
}

[Fact]
public async Task Multihop_dedupes_the_flat_passage_union()
{
    var rag = Substitute.For<IRagRetrievalService>();
    // same passage returned for BOTH topics
    rag.SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>())
        .Returns(Results(("shared rule text", "PlayerHandbook 2014")));
    var svc = new RulesAdjudicationService(rag);

    var result = await svc.AskAsync("q", ruleTopics: ["a", "b"], edition: null, CancellationToken.None);

    result.Passages.Should().ContainSingle();              // deduped flat union
    result.Topics.Should().HaveCount(2);                   // but retained under each topic
    result.Topics.Should().OnlyContain(t => t.Passages.Count == 1);
}

[Fact]
public async Task No_topics_is_single_shot_with_empty_grouping()
{
    var rag = Substitute.For<IRagRetrievalService>();
    rag.SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>())
        .Returns(Results(("Grappling", "PlayerHandbook 2014")));
    var svc = new RulesAdjudicationService(rag);

    var result = await svc.AskAsync("grappling", ruleTopics: null, edition: null, CancellationToken.None);

    result.Topics.Should().BeEmpty();
    result.Passages.Should().ContainSingle();
    await rag.Received(1).SearchAsync(
        Arg.Is<RetrievalQuery>(q => q.QueryText == "grappling" && q.TopK == RuleSources.TopK),
        Arg.Any<CancellationToken>());
}
```

(Adjust the `Results` helper's `ci =>` lambda to the real NSubstitute `Returns(Func)` form and `RetrievalResult`/`ChunkMetadata` shapes already used in this file.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~RulesAdjudicationServiceTests"`
Expected: FAIL — `AskAsync` has no `ruleTopics` param; `RulesRulingResult` has no `Topics`; `RuleTopicPassages`/`TopicTopK` missing.

- [ ] **Step 3: Add `TopicTopK`**

In `RuleSources.cs`, add:

```csharp
    /// <summary>Per-topic result cap for multi-hop retrieval, so N topics don't balloon the passage count.</summary>
    public const int TopicTopK = 5;
```

- [ ] **Step 4: Extend the result shape**

Replace `RulesRulingResult.cs` body:

```csharp
using DndMcpAICsharpFun.Features.Lore; // CitedPassage

namespace DndMcpAICsharpFun.Features.Rules;

public sealed record RulesRulingResult(
    IReadOnlyList<CitedPassage> Passages,
    IReadOnlyCollection<string> ScopedBooks,
    IReadOnlyList<RuleTopicPassages> Topics);

/// <summary>The passages grounding one named rule in a multi-hop rules query.</summary>
public sealed record RuleTopicPassages(string Topic, IReadOnlyList<CitedPassage> Passages);
```

- [ ] **Step 5: Rewrite AskAsync for multi-hop**

Replace `RulesAdjudicationService.AskAsync` with:

```csharp
    public async Task<RulesRulingResult> AskAsync(
        string question, IReadOnlyList<string>? ruleTopics, DndVersion? edition, CancellationToken ct)
    {
        // Single-shot (v1) when the caller didn't decompose the question.
        if (ruleTopics is not { Count: > 0 })
        {
            var single = await RetrieveAsync(question, edition, RuleSources.TopK, ct);
            return new RulesRulingResult(single, RuleSources.Books, []);
        }

        // Multi-hop: ground each named rule with its own scoped retrieval.
        var topicGroups = new List<RuleTopicPassages>(ruleTopics.Count);
        foreach (var topic in ruleTopics)
        {
            var topicPassages = await RetrieveAsync(topic, edition, RuleSources.TopicTopK, ct);
            topicGroups.Add(new RuleTopicPassages(topic, topicPassages));
        }

        // Flat union de-duped by citation identity, keeping the highest-scoring copy; the per-topic
        // groups above still retain each passage under every rule it grounded.
        var merged = topicGroups
            .SelectMany(g => g.Passages)
            .GroupBy(p => (p.Text, p.SourceBook, p.Section))
            .Select(grp => grp.OrderByDescending(p => p.Score).First())
            .ToList();

        return new RulesRulingResult(merged, RuleSources.Books, topicGroups);
    }

    private async Task<IReadOnlyList<CitedPassage>> RetrieveAsync(
        string query, DndVersion? edition, int topK, CancellationToken ct)
    {
        var q = new RetrievalQuery(query, Version: edition, TopK: topK, SourceBooks: RuleSources.Books);
        var results = await rag.SearchAsync(q, ct);
        return results.Select(r => new CitedPassage(
            r.Text, r.Metadata.SourceBook, r.Metadata.SectionTitle ?? r.Metadata.Chapter, r.Score)).ToList();
    }
```

(Add `using System.Linq;` if not covered by implicit usings — it is, via `ImplicitUsings`.)

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~RulesAdjudicationServiceTests"`
Expected: PASS (the 3 v1 tests with `ruleTopics: null` + the 3 new multi-hop tests).

- [ ] **Step 7: Commit**

```bash
git add Features/Rules/ DndMcpAICsharpFun.Tests/Rules/RulesAdjudicationServiceTests.cs
git commit -m "feat(rules): multi-hop per-topic retrieval (ruleTopics) with grouped + deduped passages"
```

---

### Task 2: ask_rules tool `ruleTopics` param

**Files:**
- Modify: `Features/Chat/DndChatService.cs` (`ask_rules` delegate + description)
- Modify: `DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs` (guard/routing)

**Interfaces:**
- Consumes: `RulesAdjudicationService.AskAsync(question, ruleTopics, edition, ct)` (Task 1).
- Produces: `ask_rules(question, ruleTopics?, edition?)` tool; still no `userId`/`campaignId`.

- [ ] **Step 1: Update the delegate + description**

Replace the `ask_rules` registration's delegate/description (`Features/Chat/DndChatService.cs:180-194`):

```csharp
            toolList.Add(AIFunctionFactory.Create(
                (string question, string[]? ruleTopics, string? edition, CancellationToken toolCt) =>
                    rulesAdjudicationService.AskAsync(
                        question,
                        ruleTopics,
                        string.IsNullOrWhiteSpace(edition) ? (DndVersion?)null : ParseEdition(edition),
                        toolCt),
                name: "ask_rules",
                description: "Answer a D&D RULES question (including multi-rule interactions like " +
                    "'can I grapple a creature that's already prone?'). Identify the DISTINCT rules the " +
                    "question involves and pass them as ruleTopics (e.g. [\"grappling\", \"prone condition\"]) " +
                    "so each rule is grounded on its own retrieval; omit ruleTopics for a simple single-rule " +
                    "question. Returns cited rule passages retrieved ONLY from the core rulebooks, grouped by " +
                    "topic. Compose your ruling STRICTLY from the returned passages: NAME each rule you " +
                    "combine and CITE it (source book + section); where the rules don't explicitly resolve an " +
                    "interaction, say so and distinguish rules-as-written from a DM ruling; if no passages are " +
                    "returned, say the rules don't directly cover it — never invent a rule. Not tied to any " +
                    "campaign or character. edition is optional (\"2014\"/\"2024\"); omit to search all editions."));
```

- [ ] **Step 2: Update the guard/routing tests**

In `DndChatServiceTests`, the existing `AskRulesTool_exposes_no_user_or_campaign_id_and_reaches_the_service` invokes the tool via `ToArgs`. Keep its schema assertions (still no `userId`/`campaignId`). Add a `ruleTopics` to the invoke args to prove the new param routes, and assert the result deserializes to a `RulesRulingResult` with empty `Passages` (empty rag) — e.g.:

```csharp
    var result = await tool.InvokeAsync(
        ToArgs(new { question = "grapple while prone", ruleTopics = new[] { "grappling", "prone condition" }, edition = (string?)null }),
        CancellationToken.None);
    var ruling = ((JsonElement)result!).Deserialize<RulesRulingResult>(tool.JsonSerializerOptions);
    ruling!.Passages.Should().BeEmpty();
    ruling.Topics.Should().HaveCount(2); // two topics were retrieved (each empty) — proves ruleTopics routed
```

(The empty-rag substitute returns empty passages per topic, so `Topics` has 2 empty groups — proving the param reached multi-hop.)

- [ ] **Step 3: Run tests**

Run: `dotnet test --filter "FullyQualifiedName~DndChatServiceTests"`
Expected: PASS — present/absent, no-userId/no-campaignId schema, `ruleTopics` routes through multi-hop.

- [ ] **Step 4: Commit**

```bash
git add Features/Chat/DndChatService.cs DndMcpAICsharpFun.Tests/Chat/DndChatServiceTests.cs
git commit -m "feat(chat): ask_rules gains ruleTopics for multi-hop per-rule grounding"
```

---

### Task 3: Real-Qdrant multi-hop non-vacuity test + full verification

**Files:**
- Modify: `DndMcpAICsharpFun.Tests/Rules/RulesScopingIntegrationTests.cs`

- [ ] **Step 1: Update the existing integration test to the new signature + add multi-hop**

The existing `AskAsync` call gains `ruleTopics: null` (behavior-preserving). Add a multi-hop test: seed a grappling rule block + a prone rule block (both `source_book="PlayerHandbook 2014"`) + an off-scope `"Monster Manual 2014"` block; call `AskAsync("...", ruleTopics: ["grappling", "prone"], null, ct)`; assert:

```csharp
result.Passages.Select(p => p.SourceBook).Should().NotContain("Monster Manual 2014"); // scope holds per topic
result.Topics.Select(t => t.Topic).Should().Equal("grappling", "prone");
result.Topics.Should().OnlyContain(t => t.Passages.Count > 0);                        // each rule grounded
```

Give the grappling and prone blocks embeddings such that each ranks top for its own topic query (or seed identical vectors and assert each topic's retrieval returns the rulebook blocks and never MM — MM is off-scope so the SourceBooks filter excludes it regardless).

- [ ] **Step 2: Run the integration test (Docker + Qdrant)**

Run: `dotnet test --filter "FullyQualifiedName~RulesScopingIntegrationTests"` (~400000ms)
Expected: PASS — per-topic grounding, MM excluded.

- [ ] **Step 3: Build + FULL suite**

Run: `dotnet build` (0/0) then `dotnet test` (the FULL suite — a result-shape change can ripple; do not filter). Record the total (was 1285/1285).

- [ ] **Step 4: Commit**

```bash
git add DndMcpAICsharpFun.Tests/Rules/RulesScopingIntegrationTests.cs
git commit -m "test(rules): real-Qdrant multi-hop grounds each topic and excludes off-scope blocks"
```

- [ ] **Step 5: Rebuild container + live smoke + final review**

`docker compose build app && docker compose up -d app`; wait healthy. Sign in as `test`, ask "can I grapple a creature that's already prone?" → the LLM passes `ruleTopics` (grappling + prone) and the ruling grounds on both, each cited (or, if the LLM omits topics, the single-shot fallback still answers). Then request a final opus whole-branch review; on READY, refresh the `companion_roadmap` memory.
