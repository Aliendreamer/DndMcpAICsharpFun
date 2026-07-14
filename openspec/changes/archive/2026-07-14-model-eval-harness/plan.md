# Model Eval Harness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a re-runnable `Tools/ModelEval` console that scores the chat model's tool-selection, MEAI arg-binding, response adherence, and latency as N-run pass-rates through the REAL MEAI `FunctionInvocation` stack against a live `OllamaChatClient`, then land `think:false` on the production chat path if the harness proves it a free win.

**Architecture:** A standalone console (mirrors `Tools/SchemaGenerator`) that ProjectReferences the main app for its MEAI + Ollama types. It re-declares the top chat tools as `AIFunction`s with **stubbed delegates** returning canned/empty results, runs a fixed scenario set N times each, and inspects `ChatResponse.Messages` (`FunctionCallContent` for selection, `FunctionResultContent.Exception` for binding) plus stub-recorded state to build a per-scenario scorecard. The decision task applies the measured think-mode to `Extensions/ChatExtensions.cs`.

**Tech Stack:** .NET 10, `Microsoft.Extensions.AI` 9.7.0 + **`OllamaSharp` 5.4.25** (its `OllamaApiClient` implements MEAI `IChatClient` and supports `think`; inherited transitively via the main-project ProjectReference), a live Ollama with `qwen3:8b` pulled — reachable in THIS environment at `http://172.18.0.10:11434` (the shared PCC container's IP; port not published to host, so `localhost:11434` does NOT work — pass `--base-url http://172.18.0.10:11434`).

> **⚠️ Task 1 spike finding (supersedes design D3 + the `/no_think` default this plan originally carried):** the top-level Ollama `think:false` request field is the only effective lever (`eval_count` 388→12); the `/no_think` directive is a near no-op on this qwen3:8b/Ollama version; and MEAI.Ollama's `OllamaChatClient` CANNOT emit `think` at all. The harness therefore uses **OllamaSharp's `OllamaApiClient`** and toggles think via `ChatOptions.RawRepresentationFactory = _ => new OllamaSharp.Models.Chat.ChatRequest { Think = false }`. See `report.md`. Tasks 2 and 5 are written to this corrected mechanism.

## Global Constraints

- **Target framework `net10.0`, nullable enabled, implicit usings, warnings-as-errors** — inherited from `Directory.Build.props`; every file in `Tools/ModelEval` must compile warning-clean.
- **Central Package Management** — csproj `PackageReference`s are version-less; versions live in `Directory.Packages.props`. Do NOT add versions to the csproj. The main-project ProjectReference already brings `Microsoft.Extensions.AI` + `OllamaSharp` transitively; no new PackageReference is needed.
- **Think toggle mechanism (spike-confirmed):** OllamaSharp `OllamaApiClient` as the `IChatClient`; think OFF = `ChatOptions.RawRepresentationFactory = _ => new OllamaSharp.Models.Chat.ChatRequest { Think = false }`; think ON = omit the factory. MEAI.Ollama's `OllamaChatClient` and the `/no_think` directive both do NOT work here — do not use them.
- **Live-Ollama tasks run with `dangerouslyDisableSandbox: true`** — the sandbox blocks the loopback Ollama call and GPU; any `dotnet run`/`dotnet build` that hits Ollama or restores packages needs it (repo memory: dotnet needs dangerouslyDisableSandbox under git-crypt).
- **Not part of app runtime or CI** — this is a bench tool. No `IsTestProject`, no test discovery, run on demand.
- **Stub tool signatures must match production `DndChatService` registrations verbatim** (same param names, nullability, and `= null` defaults) so the MEAI binder behaves identically — that binder is the whole point (the `calculate_crafting` missing-required-arg bug lived there). CancellationToken params may be dropped from stubs (AIFunctionFactory strips them from the model-facing schema anyway).
- **`.http` / `.insomnia.json` rule does NOT apply** — this change adds no HTTP endpoint. The one production edit (Task 5) is chat-client inference config, not a route.

---

### Task 1: Spike — how MEAI `OllamaChatClient` suppresses qwen3 thinking

**Files:**
- Create (temporary, deleted at end of task): `Tools/ModelEval/ThinkSpike.cs` scratch — OR run inline in this session. No committed code from this task except the recorded finding.
- Reference: `Features/Ingestion/EntityExtraction/OllamaEntityExtractionClient.cs:19-23` (proven mechanism: append `\n\n/no_think` to the user turn).

**Interfaces:**
- Produces: a documented `ApplyThinkOff(ChatOptions options, List<ChatMessage> messages, bool thinkOn)` mechanism that Task 2/3 (`ModelClientFactory` / scenario runner) and Task 5 (production chat path) both consume. Record the EXACT working call in the change dir.

**Context:** The design (D3) hypothesised `ChatOptions.AdditionalProperties["think"] = false`. The repo already has a PROVEN mechanism — the `/no_think` directive appended to the user message (`OllamaEntityExtractionClient`). This spike confirms which one actually suppresses the `<think>` block through the MEAI `OllamaChatClient` (9.7-preview) so the harness measures a real think-off, not a no-op.

- [ ] **Step 1: Confirm Ollama is up and `qwen3:8b` is pulled**

Run (dangerouslyDisableSandbox): `curl -s http://localhost:11434/api/tags | grep -o 'qwen3:8b'`
Expected: prints `qwen3:8b`. If empty, pull it first: `ollama pull qwen3:8b` (or via the app's `ollama-pull` admin route) — do not proceed until present.

- [ ] **Step 2: Probe think-ON baseline via raw Ollama (establishes the `<think>` fingerprint)**

Run (dangerouslyDisableSandbox):
```bash
curl -s http://localhost:11434/api/chat -d '{
  "model":"qwen3:8b",
  "messages":[{"role":"user","content":"In one sentence, is a longsword a martial weapon?"}],
  "stream":false
}' | python3 -c "import sys,json;d=json.load(sys.stdin);print('THINK_ON eval_count',d.get('eval_count'));print(repr(d['message']['content'][:200]))"
```
Expected: `content` contains a `<think>` block (or the model streams reasoning); note `eval_count`.

- [ ] **Step 3: Probe think-OFF candidate A — top-level `think:false` on the raw API**

Run (dangerouslyDisableSandbox):
```bash
curl -s http://localhost:11434/api/chat -d '{
  "model":"qwen3:8b",
  "think":false,
  "messages":[{"role":"user","content":"In one sentence, is a longsword a martial weapon?"}],
  "stream":false
}' | python3 -c "import sys,json;d=json.load(sys.stdin);print('THINK_OFF(think:false) eval_count',d.get('eval_count'));print(repr(d['message']['content'][:200]))"
```
Expected: NO `<think>` block, materially lower `eval_count`. Records that the Ollama API honours `think:false`.

- [ ] **Step 4: Probe think-OFF candidate B — `/no_think` directive (the in-repo mechanism)**

Run (dangerouslyDisableSandbox):
```bash
curl -s http://localhost:11434/api/chat -d '{
  "model":"qwen3:8b",
  "messages":[{"role":"user","content":"In one sentence, is a longsword a martial weapon? /no_think"}],
  "stream":false
}' | python3 -c "import sys,json;d=json.load(sys.stdin);print('THINK_OFF(/no_think) eval_count',d.get('eval_count'));print(repr(d['message']['content'][:200]))"
```
Expected: NO `<think>` block (or empty `<think></think>`), low `eval_count`. Confirms the proven directive.

- [ ] **Step 5: Decide the MEAI mechanism**

Decision rule for `ApplyThinkOff`:
- If MEAI `OllamaChatClient` (9.7-preview) forwards `ChatOptions.AdditionalProperties["think"] = false` to the API `think` field → prefer that (cleanest; no prompt mutation). Task 2 will empirically confirm it against the harness in Step during Task 4's first run.
- If unsure / it does not forward → use the PROVEN `/no_think` mechanism: append `"\n\n/no_think"` to the LAST user message's text. This is what production extraction uses and is guaranteed to work.

Default the plan to the `/no_think` mechanism (append to last user turn) unless Step 3+MEAI confirm the property path. Record the chosen mechanism in the change dir.

- [ ] **Step 6: Record the finding**

Append to `openspec/changes/model-eval-harness/report.md` (create it) a short "Think mechanism" section: which candidate suppresses `<think>`, the raw eval_count delta (on vs off), and the exact `ApplyThinkOff` approach the harness+production will use. No commit yet (folded into Task 2's commit) OR commit as `docs(model-eval-harness): think-mechanism spike finding`.

---

### Task 2: `Tools/ModelEval` console scaffold + client factory

**Files:**
- Create: `Tools/ModelEval/ModelEval.csproj`
- Create: `Tools/ModelEval/EvalArgs.cs`
- Create: `Tools/ModelEval/ModelClientFactory.cs`
- Create: `Tools/ModelEval/Program.cs`

**Interfaces:**
- Produces:
  - `record EvalArgs(string Model, bool ThinkOn, int Runs, string BaseUrl)` with `static EvalArgs Parse(string[] args)`.
  - `static class ModelClientFactory` → `IChatClient Build(EvalArgs a)` returning `new OllamaApiClient(new Uri(a.BaseUrl), a.Model).AsBuilder().UseFunctionInvocation().Build()`, and `static ChatOptions BuildOptions(bool thinkOn, IList<AITool>? tools)` which sets `RawRepresentationFactory = _ => new ChatRequest { Think = false }` when think is OFF (spike-confirmed mechanism).
- Consumes: the Task 1 spike mechanism (OllamaSharp + `RawRepresentationFactory`).

- [ ] **Step 1: Write the csproj**

Create `Tools/ModelEval/ModelEval.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <RootNamespace>DndMcpAICsharpFun.Tools.ModelEval</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\DndMcpAICsharpFun.csproj" />
  </ItemGroup>
</Project>
```
(MEAI + Ollama types arrive transitively via the ProjectReference — mirrors `Tools/SchemaGenerator`.)

- [ ] **Step 2: Write `EvalArgs.cs`**

Create `Tools/ModelEval/EvalArgs.cs`:
```csharp
namespace DndMcpAICsharpFun.Tools.ModelEval;

internal sealed record EvalArgs(string Model, bool ThinkOn, int Runs, string BaseUrl)
{
    public static EvalArgs Parse(string[] args)
    {
        var model = "qwen3:8b";
        var thinkOn = true;
        var runs = 5;
        var baseUrl = "http://localhost:11434";

        for (var i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--model": model = args[++i]; break;
                case "--think": thinkOn = !string.Equals(args[++i], "off", StringComparison.OrdinalIgnoreCase); break;
                case "--runs": runs = int.Parse(args[++i]); break;
                case "--base-url": baseUrl = args[++i]; break;
            }
        }

        return new EvalArgs(model, thinkOn, runs, baseUrl);
    }
}
```

- [ ] **Step 3: Write `ModelClientFactory.cs`**

Create `Tools/ModelEval/ModelClientFactory.cs` (spike-confirmed mechanism — OllamaSharp `OllamaApiClient` + `RawRepresentationFactory` → `ChatRequest.Think`):
```csharp
using Microsoft.Extensions.AI;

using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace DndMcpAICsharpFun.Tools.ModelEval;

internal static class ModelClientFactory
{
    // OllamaApiClient (NOT MEAI.Ollama's OllamaChatClient) — only OllamaSharp can send the
    // top-level `think` field, and it implements MEAI IChatClient so the FunctionInvocation
    // stack wraps it identically to production.
    public static IChatClient Build(EvalArgs a) =>
        new OllamaApiClient(new Uri(a.BaseUrl), a.Model)
            .AsBuilder()
            .UseFunctionInvocation()
            .Build();

    /// <summary>
    /// Build the per-call ChatOptions. When think is OFF, set a RawRepresentationFactory that
    /// hands OllamaSharp a ChatRequest template with Think=false — its AbstractionMapper copies
    /// that into the outgoing request's top-level `think` field (spike: eval_count 388 -> 12).
    /// When think is ON, omit the factory (qwen3 thinks by default).
    /// </summary>
    public static ChatOptions BuildOptions(bool thinkOn, IList<AITool>? tools)
    {
        var options = new ChatOptions();
        if (tools is not null) options.Tools = [.. tools];
        if (!thinkOn) options.RawRepresentationFactory = _ => new ChatRequest { Think = false };
        return options;
    }
}
```

- [ ] **Step 4: Write a minimal `Program.cs` (hello round-trip)**

Create `Tools/ModelEval/Program.cs`:
```csharp
using DndMcpAICsharpFun.Tools.ModelEval;

using Microsoft.Extensions.AI;

var a = EvalArgs.Parse(args);
Console.WriteLine($"ModelEval — model={a.Model} think={(a.ThinkOn ? "on" : "off")} runs={a.Runs} base={a.BaseUrl}");

var client = ModelClientFactory.Build(a);
var messages = new List<ChatMessage> { new(ChatRole.User, "Say hello in exactly three words.") };
var options = ModelClientFactory.BuildOptions(a.ThinkOn, tools: null);

var response = await client.GetResponseAsync(messages, options);
Console.WriteLine("REPLY: " + (response.Text ?? "(none)"));
return 0;
```

- [ ] **Step 5: Build the console**

Run (dangerouslyDisableSandbox): `dotnet build Tools/ModelEval/ModelEval.csproj`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. Fix any warnings (warnings-as-errors).

- [ ] **Step 6: Run the hello round-trip against live Ollama**

Run (dangerouslyDisableSandbox): `dotnet run --project Tools/ModelEval/ModelEval.csproj -- --runs 1 --base-url http://172.18.0.10:11434`
Expected: prints the config header and `REPLY: <three-ish words>`. Confirms OllamaSharp MEAI client → Ollama end-to-end.

- [ ] **Step 7: Commit**

```bash
git add Tools/ModelEval/ openspec/changes/model-eval-harness/report.md
git commit -m "feat(model-eval-harness): ModelEval console scaffold + client factory + think spike finding"
```

---

### Task 3: Stub tools + scenario set

**Files:**
- Create: `Tools/ModelEval/StubState.cs`
- Create: `Tools/ModelEval/StubTools.cs`
- Create: `Tools/ModelEval/Scenario.cs`
- Create: `Tools/ModelEval/Scenarios.cs`

**Interfaces:**
- Produces:
  - `static class StubState` → `static bool AskRulesReturnsEmpty` (per-scenario flag; the runner sets it before each `GetResponseAsync`) and `static readonly List<string> InvokedTools` with `static void Reset()`.
  - `static class StubTools` → `IList<AITool> Build()` returning the 8 stubbed `AIFunction`s.
  - `record Scenario(string Name, string Prompt, string? ExpectedTool, bool StubEmpty, Func<string, bool> AdherenceCheck)`.
  - `static class Scenarios` → `IReadOnlyList<Scenario> All`.
- Consumes: nothing beyond MEAI. Task 4's runner consumes `StubTools.Build()`, `Scenarios.All`, and `StubState`.

**Context:** Signatures mirror `Features/Chat/DndChatService.cs` registrations verbatim (minus CancellationToken). Stubs record their own invocation into `StubState.InvokedTools` and read `StubState.AskRulesReturnsEmpty` for the fabrication case. Canned values match design D4 (`750`/`2000` for crafting; a cited passage naming a book; a ≥3-member ensemble).

- [ ] **Step 1: Write `StubState.cs`**

Create `Tools/ModelEval/StubState.cs`:
```csharp
namespace DndMcpAICsharpFun.Tools.ModelEval;

/// <summary>
/// Cross-call scratch state for the stub tools. Scenarios run strictly sequentially (one scenario,
/// N sequential runs), so plain statics are safe — no concurrency. The runner calls <see cref="Reset"/>
/// before each run and sets <see cref="AskRulesReturnsEmpty"/> from the scenario's StubEmpty flag.
/// </summary>
internal static class StubState
{
    public static readonly List<string> InvokedTools = [];
    public static bool AskRulesReturnsEmpty;

    public static void Reset()
    {
        InvokedTools.Clear();
        AskRulesReturnsEmpty = false;
    }
}
```

- [ ] **Step 2: Write `StubTools.cs`**

> **CORRECTION (shipped as fix `0ede031`):** the code block below mistakenly adds `= null` defaults to
> 6 tools' optional params. Production `DndChatService` does NOT have those defaults, and a
> nullable-without-default param is marked *required* by AIFunctionFactory — so verbatim parity means
> **remove `= null` from `generate_npc.maxCr`, `ask_rules.{ruleTopics,edition}`, `plan_downtime.edition`,
> `build_encounter.{theme,maxCr,minCr}`, `ask_setting_lore.edition`, `plan_level_up.{targetClass,considerDip}`**.
> Only `calculate_crafting` keeps its `= null` defaults (production has them). This makes the harness
> faithfully reproduce production's binder — and likely exposes a latent binding bug on those 6 tools.

Create `Tools/ModelEval/StubTools.cs` — 8 stubs, signatures matching production verbatim (names/params/CancellationToken dropped; `= null` ONLY where production has it, i.e. `calculate_crafting`):
```csharp
using Microsoft.Extensions.AI;

namespace DndMcpAICsharpFun.Tools.ModelEval;

internal static class StubTools
{
    public static IList<AITool> Build() =>
    [
        AIFunctionFactory.Create(
            (int? marketValue = null, string? rarity = null, int? crafters = null) =>
            {
                StubState.InvokedTools.Add("calculate_crafting");
                var hasValue = marketValue.HasValue;
                var hasRarity = !string.IsNullOrWhiteSpace(rarity);
                if (hasValue == hasRarity)
                    return (object)new { error = "Supply exactly ONE of marketValue or rarity." };
                if (hasValue)
                    return new { kind = "nonmagical", materialsGp = 750, totalWorkweeks = 30, days = 150, citation = "Xanathar's Guide to Everything — Crafting" };
                return new { kind = "magic-item", rarity = rarity, workweeks = 10, goldCostGp = 2000, citation = "Xanathar's Guide to Everything — Crafting Magic Items" };
            },
            name: "calculate_crafting",
            description: "Calculate the EXACT time and cost to CRAFT an item. For a NONMAGICAL item pass its marketValue (gold) and optionally crafters; for a MAGIC item pass its rarity (common/uncommon/rare/very rare/legendary). Supply exactly ONE of marketValue or rarity. Report the returned numbers EXACTLY; never re-derive."),

        AIFunctionFactory.Create(
            (string concept, string archetype, double? maxCr = null) =>
            {
                StubState.InvokedTools.Add("generate_npc");
                return (object)new { name = "Garret Vosk", archetype, archetypeInCorpus = true, cr = "1", hp = 27, source = "Monster Manual", block = "Spy (CR 1): AC 12, HP 27, ..." };
            },
            name: "generate_npc",
            description: "Generate an NPC for a scene from a concept. YOU pick the stat-block archetype and pass it as archetype (e.g. Spy, Guard, Bandit Captain); the tool returns that archetype's REAL stat block. Take ALL mechanical stats from the returned block and CITE its source; never invent stat numbers."),

        AIFunctionFactory.Create(
            (string theme) =>
            {
                StubState.InvokedTools.Add("generate_npc_party");
                return (object)new
                {
                    theme,
                    members = new[]
                    {
                        new { role = "leader", name = "Mara Kell", archetype = "Bandit Captain", source = "Monster Manual" },
                        new { role = "support", name = "Dross", archetype = "Thug", source = "Monster Manual" },
                        new { role = "support", name = "Wren", archetype = "Spy", source = "Monster Manual" },
                    },
                };
            },
            name: "generate_npc_party",
            description: "Generate a themed CAST of NPCs from a single theme string (e.g. 'a Sharn heist crew'). Returns an ENSEMBLE — a leader plus supporting members — each anchored to a REAL stat block. CITE each block's source; never invent stat numbers."),

        AIFunctionFactory.Create(
            (string question, string[]? ruleTopics = null, string? edition = null) =>
            {
                StubState.InvokedTools.Add("ask_rules");
                if (StubState.AskRulesReturnsEmpty)
                    return (object)new { passages = Array.Empty<object>() };
                return new { passages = new[] { new { text = "You can grapple a prone creature; prone does not prevent being grappled.", source = "Player's Handbook", section = "Combat" } } };
            },
            name: "ask_rules",
            description: "Answer a D&D RULES question (including multi-rule interactions). Identify the DISTINCT rules and pass them as ruleTopics (e.g. [\"grappling\", \"prone condition\"]); omit for a simple single-rule question. Returns cited rule passages from the core rulebooks. Compose STRICTLY from the returned passages and CITE each; if no passages are returned, say the rules don't cover it — never invent a rule."),

        AIFunctionFactory.Create(
            (string activity, string? edition = null) =>
            {
                StubState.InvokedTools.Add("plan_downtime");
                return (object)new { passages = new[] { new { text = "Crafting a nonmagical item costs workweeks equal to its value / 50 gp.", source = "Xanathar's Guide to Everything", section = "Downtime" } } };
            },
            name: "plan_downtime",
            description: "Plan a D&D DOWNTIME activity (crafting, training, carousing, research, etc.). Pass the activity as free text. Returns cited rule passages from the downtime rulebooks (Xanathar's + DMG). Compose STRICTLY from the returned passages and CITE each; never invent times or costs."),

        AIFunctionFactory.Create(
            (long? campaignId, string difficulty, string edition, string? theme = null, double? maxCr = null, double? minCr = null) =>
            {
                StubState.InvokedTools.Add("build_encounter");
                return (object)new { difficulty, monsters = new[] { new { name = "Hobgoblin", count = 1, cr = "0.5", source = "Monster Manual" }, new { name = "Goblin", count = 6, cr = "0.25", source = "Monster Manual" } } };
            },
            name: "build_encounter",
            description: "Build a combat encounter for a target difficulty (Trivial/Easy/Medium/Hard/Deadly) and optional theme, for the signed-in user's party (campaignId). Builds swarms — a strong anchor plus multiples of cheaper monsters — returned grouped as {monster, count}. edition is \"2014\" or \"2024\"."),

        AIFunctionFactory.Create(
            (long campaignId, string question, string? edition = null) =>
            {
                StubState.InvokedTools.Add("ask_setting_lore");
                return (object)new { passages = new[] { new { text = "Sharn, the City of Towers, is governed by a Lord Mayor and city council.", source = "Eberron: Rising from the Last War", section = "Sharn" } } };
            },
            name: "ask_setting_lore",
            description: "Answer a lore/worldbuilding question for one of the signed-in user's own campaigns, scoped to that campaign's SETTING sources. Pass campaignId and the question. Returns cited passages from the campaign's setting books. Compose STRICTLY from the returned passages and CITE each; never invent world lore."),

        AIFunctionFactory.Create(
            (long heroSnapshotId, string? targetClass = null, bool? considerDip = null) =>
            {
                StubState.InvokedTools.Add("plan_level_up");
                return (object)new { className = "Fighter", nextLevel = 5, hpDelta = 7, features = new[] { "Extra Attack" }, source = "Player's Handbook" };
            },
            name: "plan_level_up",
            description: "Plan the next level-up for a hero snapshot the signed-in user owns (heroSnapshotId). Returns the rule-grounded delta (HP, proficiency, features, spell slots) and cited options. RECOMMEND a pick ONLY from the returned options; never invent a feat, subclass, or spell."),
    ];
}
```
(Eight stubs cover every scenario's `ExpectedTool` incl. the negative/empty cases; `plan_level_up` is included so a hero-scoped prompt has a real competing tool, tightening the selection signal.)

- [ ] **Step 3: Write `Scenario.cs`**

Create `Tools/ModelEval/Scenario.cs`:
```csharp
namespace DndMcpAICsharpFun.Tools.ModelEval;

/// <param name="ExpectedTool">Tool name the model SHOULD select, or null for the negative (no-tool) case.</param>
/// <param name="StubEmpty">When true, the runner sets StubState.AskRulesReturnsEmpty so ask_rules returns no passages (fabrication test).</param>
/// <param name="AdherenceCheck">Runs against the final assistant text; true = adhered.</param>
internal sealed record Scenario(
    string Name,
    string Prompt,
    string? ExpectedTool,
    bool StubEmpty,
    Func<string, bool> AdherenceCheck);
```

- [ ] **Step 4: Write `Scenarios.cs`** (design D4 set)

Create `Tools/ModelEval/Scenarios.cs`:
```csharp
namespace DndMcpAICsharpFun.Tools.ModelEval;

internal static class Scenarios
{
    private static bool Has(string text, string needle) =>
        text.Contains(needle, StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<Scenario> All { get; } =
    [
        new("craft-nonmagical", "I want to craft a suit of plate armor with a market value of 1500 gp. How long and how much?",
            "calculate_crafting", false, t => Has(t, "750")),
        new("craft-magic", "How long does it take to craft a rare magic item?",
            "calculate_crafting", false, t => Has(t, "2000")),
        new("npc-party", "Give me a criminal gang to run a heist against the party.",
            "generate_npc_party", false, t => Has(t, "Mara") || Has(t, "leader") || Has(t, "member")),
        new("npc-single", "I need a single shifty dockworker NPC for this scene.",
            "generate_npc", false, t => Has(t, "Garret") || Has(t, "Spy")),
        new("rules-grapple", "Can I grapple a creature that is already prone?",
            "ask_rules", false, t => Has(t, "Player's Handbook") || Has(t, "PHB")),
        new("downtime-craft", "How long would it take to craft this during downtime between adventures?",
            "plan_downtime", false, t => Has(t, "Xanathar")),
        new("encounter-build", "Build a hard combat encounter for my party.",
            "build_encounter", false, t => Has(t, "Hobgoblin") || Has(t, "Goblin")),
        new("setting-lore", "Who rules the city of Sharn in my Eberron campaign?",
            "ask_setting_lore", false, t => Has(t, "Lord Mayor") || Has(t, "council")),
        new("rules-empty", "What are the exact rules for holding your breath while swimming in lava?",
            "ask_rules", true, t => Has(t, "not") || Has(t, "don't cover") || Has(t, "no ") || Has(t, "isn't")),
        new("no-tool", "Hi there, how's it going today?",
            null, false, _ => true),
    ];
}
```
(The `no-tool` adherence check is trivially true — for the negative case, SELECTION carries the signal: expected tool is null, so "adhered" collapses to "selected no tool", scored in Task 4.)

- [ ] **Step 5: Build**

Run (dangerouslyDisableSandbox): `dotnet build Tools/ModelEval/ModelEval.csproj`
Expected: `0 Warning(s) 0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add Tools/ModelEval/
git commit -m "feat(model-eval-harness): stubbed chat tools + scenario set"
```

---

### Task 4: Scorecard — N-run scoring + table

**Files:**
- Create: `Tools/ModelEval/RunResult.cs`
- Create: `Tools/ModelEval/ScenarioRunner.cs`
- Create: `Tools/ModelEval/Scorecard.cs`
- Modify: `Tools/ModelEval/Program.cs` (replace the hello round-trip with the full run)

**Interfaces:**
- Produces:
  - `record RunResult(string? SelectedTool, bool BindOk, bool Adhered, long WallMs, long? FirstToolCallMs)`.
  - `static class ScenarioRunner` → `Task<RunResult> RunOnceAsync(IChatClient client, Scenario s, bool thinkOn, string persona)`.
  - `static class Scorecard` → `void Print(EvalArgs a, IReadOnlyList<(Scenario Scenario, IReadOnlyList<RunResult> Runs)> results)`.
- Consumes: `StubTools.Build()`, `Scenarios.All`, `StubState`, `ModelClientFactory`.

**Context:** Selection + binding are read from `ChatResponse.Messages`: a `FunctionCallContent` gives the model's chosen tool (pre-binding); the matching `FunctionResultContent.Exception` (non-null) OR a thrown `GetResponseAsync` means bind-fail. `StubState.InvokedTools` confirms the delegate actually ran. A persona system prompt is prepended so selection behaviour matches production; use a minimal inline persona (the real one lives in the git-crypt `Config/personas/companion.md` and is not needed for a binding/selection bench).

- [ ] **Step 1: Write `RunResult.cs`**

Create `Tools/ModelEval/RunResult.cs`:
```csharp
namespace DndMcpAICsharpFun.Tools.ModelEval;

internal sealed record RunResult(
    string? SelectedTool,
    bool BindOk,
    bool Adhered,
    long WallMs,
    long? FirstToolCallMs);
```

- [ ] **Step 2: Write `ScenarioRunner.cs`**

Create `Tools/ModelEval/ScenarioRunner.cs`:
```csharp
using System.Diagnostics;

using Microsoft.Extensions.AI;

namespace DndMcpAICsharpFun.Tools.ModelEval;

internal static class ScenarioRunner
{
    public static async Task<RunResult> RunOnceAsync(IChatClient client, Scenario s, bool thinkOn, string persona)
    {
        StubState.Reset();
        StubState.AskRulesReturnsEmpty = s.StubEmpty;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, persona),
            new(ChatRole.User, s.Prompt),
        };
        var options = ModelClientFactory.BuildOptions(thinkOn, StubTools.Build());

        var sw = Stopwatch.StartNew();
        string? selected = null;
        var bindOk = false;
        var adhered = false;
        long? firstToolMs = null;

        try
        {
            var response = await client.GetResponseAsync(messages, options);
            sw.Stop();

            // Selection: the model's chosen tool, read pre-binding from the round-trip messages.
            var call = response.Messages
                .SelectMany(m => m.Contents)
                .OfType<FunctionCallContent>()
                .FirstOrDefault();
            selected = call?.Name;

            // Binding: a result with no Exception AND the stub delegate recorded the invocation.
            var result = response.Messages
                .SelectMany(m => m.Contents)
                .OfType<FunctionResultContent>()
                .FirstOrDefault(r => r.CallId == call?.CallId);
            bindOk = selected is not null
                && result?.Exception is null
                && StubState.InvokedTools.Contains(selected);

            if (selected is not null)
                firstToolMs = sw.ElapsedMilliseconds; // whole round-trip incl. tool call; finer split not needed v1

            adhered = s.ExpectedTool is null
                ? selected is null                                  // negative case: adhered == picked no tool
                : selected == s.ExpectedTool && s.AdherenceCheck(response.Text ?? string.Empty);
        }
        catch (Exception) // MEAI binder throw (e.g. missing required arg) → bind-fail for this run
        {
            sw.Stop();
            // selected may have been captured pre-throw in a fuller impl; v1 records it as a bind failure.
        }

        return new RunResult(selected, bindOk, adhered, sw.ElapsedMilliseconds, firstToolMs);
    }
}
```

- [ ] **Step 3: Write `Scorecard.cs`**

Create `Tools/ModelEval/Scorecard.cs`:
```csharp
using System.Text;

namespace DndMcpAICsharpFun.Tools.ModelEval;

internal static class Scorecard
{
    public static void Print(EvalArgs a, IReadOnlyList<(Scenario Scenario, IReadOnlyList<RunResult> Runs)> results)
    {
        var n = a.Runs;
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine($"=== Scorecard — model={a.Model} think={(a.ThinkOn ? "on" : "off")} runs={n} ===");
        sb.AppendLine($"{"scenario",-18} {"expect",-20} {"sel",-6} {"bind",-6} {"adhere",-7} {"p50ms",-8} {"p95ms",-8}");

        var totSel = 0; var totBind = 0; var totAdhere = 0; var denom = 0;

        foreach (var (scenario, runs) in results)
        {
            var expected = scenario.ExpectedTool ?? "(none)";
            var sel = runs.Count(r => scenario.ExpectedTool is null ? r.SelectedTool is null : r.SelectedTool == scenario.ExpectedTool);
            var bind = runs.Count(r => r.BindOk || scenario.ExpectedTool is null);
            var adhere = runs.Count(r => r.Adhered);
            var p50 = Percentile(runs.Select(r => r.WallMs).ToList(), 50);
            var p95 = Percentile(runs.Select(r => r.WallMs).ToList(), 95);

            sb.AppendLine($"{scenario.Name,-18} {expected,-20} {sel + "/" + n,-6} {bind + "/" + n,-6} {adhere + "/" + n,-7} {p50,-8} {p95,-8}");
            totSel += sel; totBind += bind; totAdhere += adhere; denom += n;
        }

        sb.AppendLine(new string('-', 70));
        sb.AppendLine($"{"TOTAL",-18} {"",-20} {totSel + "/" + denom,-6} {totBind + "/" + denom,-6} {totAdhere + "/" + denom,-7}");
        Console.WriteLine(sb.ToString());
    }

    private static long Percentile(List<long> values, int p)
    {
        if (values.Count == 0) return 0;
        values.Sort();
        var idx = (int)Math.Ceiling(p / 100.0 * values.Count) - 1;
        return values[Math.Clamp(idx, 0, values.Count - 1)];
    }
}
```

- [ ] **Step 4: Rewrite `Program.cs` to run the full scorecard**

Replace `Tools/ModelEval/Program.cs` contents:
```csharp
using DndMcpAICsharpFun.Tools.ModelEval;

const string persona =
    "You are a D&D companion assistant. When the user's request matches one of your tools, CALL " +
    "the tool and answer STRICTLY from its result — never fabricate numbers, stat blocks, or citations. " +
    "For casual conversation with no tool match, just reply normally without calling a tool. " +
    "Report tool results in prose, not as numbered lists.";

var a = EvalArgs.Parse(args);
var client = ModelClientFactory.Build(a);

var results = new List<(Scenario, IReadOnlyList<RunResult>)>();
foreach (var scenario in Scenarios.All)
{
    var runs = new List<RunResult>();
    for (var i = 0; i < a.Runs; i++)
    {
        Console.Error.WriteLine($"[{scenario.Name}] run {i + 1}/{a.Runs}...");
        runs.Add(await ScenarioRunner.RunOnceAsync(client, scenario, a.ThinkOn, persona));
    }
    results.Add((scenario, runs));
}

Scorecard.Print(a, results);
return 0;
```

- [ ] **Step 5: Build**

Run (dangerouslyDisableSandbox): `dotnet build Tools/ModelEval/ModelEval.csproj`
Expected: `0 Warning(s) 0 Error(s)`.

- [ ] **Step 6: Smoke the scorecard with a tiny run**

Run (dangerouslyDisableSandbox): `dotnet run --project Tools/ModelEval/ModelEval.csproj -- --runs 1 --base-url http://172.18.0.10:11434`
Expected: progress lines on stderr, then a scorecard table with 10 scenario rows + a TOTAL row. Sanity: `craft-nonmagical` selects `calculate_crafting` and adheres (contains 750); `no-tool` selects `(none)`. If a row is all-zero selection, inspect whether the stub/persona wiring is off before proceeding.

- [ ] **Step 7: Commit**

```bash
git add Tools/ModelEval/
git commit -m "feat(model-eval-harness): N-run scorecard scoring + table"
```

- [ ] **Step 8: Manual bench run — capture the three scorecards (controller/orchestrator does this)**

Run each (dangerouslyDisableSandbox), tee output to the change dir (`B=http://172.18.0.10:11434`):
```bash
dotnet run --project Tools/ModelEval/ModelEval.csproj -- --model qwen3:8b --think on  --runs 5 --base-url http://172.18.0.10:11434 | tee openspec/changes/model-eval-harness/scorecard-qwen3-thinkon.txt
dotnet run --project Tools/ModelEval/ModelEval.csproj -- --model qwen3:8b --think off --runs 5 --base-url http://172.18.0.10:11434 | tee openspec/changes/model-eval-harness/scorecard-qwen3-thinkoff.txt
```
Optional lever-C same-size alternative IF pulled (e.g. `ollama pull qwen2.5:7b` first):
```bash
dotnet run --project Tools/ModelEval/ModelEval.csproj -- --model qwen2.5:7b --think off --runs 5 --base-url http://172.18.0.10:11434 | tee openspec/changes/model-eval-harness/scorecard-qwen25-7b.txt
```
Expected: three (or two) scorecards saved. These are the decision inputs for Task 5. Commit them:
```bash
git add openspec/changes/model-eval-harness/scorecard-*.txt
git commit -m "docs(model-eval-harness): captured bench scorecards"
```

---

### Task 5: Land the measured think decision

> **⚠️ GATE — orchestrator + explicit user sign-off before this task.** The Task 1 spike proved
> think-off requires SWAPPING the production chat client (`OllamaChatClient` → OllamaSharp
> `OllamaApiClient`) — bigger than the design's assumed "one-line change". Do not dispatch this task
> to an implementer until the bench result is in AND the user has approved the client swap.

**Files:**
- Modify (conditionally, only if think-off wins): `Extensions/ChatExtensions.cs:41-45` (swap the chat `IChatClient` from `OllamaChatClient` to `OllamaApiClient`).
- Modify (conditionally): `Features/Chat/DndChatService.cs:328-331` (set `RawRepresentationFactory` for `Think=false` on the `ChatOptions` passed to `GetResponseAsync`).
- Modify: `openspec/changes/model-eval-harness/report.md` (the decision + scorecards).

**Interfaces:**
- Consumes: the scorecards from Task 4 Step 8.
- Produces: either a `think:false` chat path (OllamaSharp client + `RawRepresentationFactory`) or a recorded "keep think-on" decision.

**Context:** Read the scorecards. Lever A wins iff `qwen3 think-off` shows a materially lower p50/p95 with **no regression** in selection/bind/adherence totals vs think-on. The production mechanism is the spike-confirmed one: OllamaSharp `OllamaApiClient` (the app's current MEAI.Ollama `OllamaChatClient` cannot send `think`) + `ChatOptions.RawRepresentationFactory = _ => new ChatRequest { Think = false }`. Both clients implement MEAI `IChatClient`, so the `.UseFunctionInvocation()` wrapper and the rest of `DndChatService` are unchanged; the chat-wiring tests mock `IChatClient` so the concrete-type swap does not touch them.

- [ ] **Step 1: Read the scorecards and decide**

Compare `scorecard-qwen3-thinkon.txt` vs `scorecard-qwen3-thinkoff.txt`. Record in `report.md`:
- p50/p95 delta (expect think-off materially faster).
- selection/bind/adherence TOTAL deltas (must be non-regressing for lever A to win).
- The verdict: LAND think-off, or KEEP think-on (and why).

If think-off REGRESSES quality → stop here; keep think-on, record the finding, skip to Step 5 (the harness still earned its keep). If it WINS → get user sign-off on the client swap, then continue.

- [ ] **Step 2 (only if think-off wins + user approved): Swap the chat client + set think-off**

(a) In `Extensions/ChatExtensions.cs`, swap the client construction (add `using OllamaSharp;`):
```csharp
        services.AddTransient<IChatClient>(_ =>
        {
            // OllamaSharp's OllamaApiClient (implements MEAI IChatClient) — unlike MEAI.Ollama's
            // OllamaChatClient it can send the top-level `think` field. The model-eval-harness bench
            // showed think-off cuts chat latency materially with no selection/binding/adherence
            // regression (see openspec/changes/model-eval-harness/report.md).
            IChatClient inner = new OllamaApiClient(new Uri(baseUrl), chatModel);
            return inner.AsBuilder().UseFunctionInvocation().Build();
        });
```
(b) In `Features/Chat/DndChatService.cs` `SendAsync`, set think-off on the request options (add `using OllamaSharp.Models.Chat;`):
```csharp
            var response = await chatClient.GetResponseAsync(
                messages,
                new ChatOptions
                {
                    Tools = [.. toolList],
                    // Suppress qwen3's thinking on the chat path (bench-proven latency win).
                    RawRepresentationFactory = _ => new ChatRequest { Think = false },
                },
                ct);
```
(No persistence change — `RawRepresentationFactory` affects only the transient outgoing request.)

- [ ] **Step 3 (only if changed): Verify chat still selects+binds and is faster**

Rebuild + re-run the app, then re-run the bench in think-off vs the live smoke:
```bash
dotnet build   # dangerouslyDisableSandbox — must be 0 warnings/errors
```
Then either the ollama-probe (repo dev-flow lesson — validate tool selection+binding without the flaky browser) OR a quick live chat smoke on a crafting/rules prompt. Confirm the reply still calls the tool and reports the real value (no fabrication) and is faster.

- [ ] **Step 4: Full test suite green**

Run (dangerouslyDisableSandbox): `dotnet test`
Expected: all tests pass (the chat-wiring tests in `DndChatServiceTests` mock `IChatClient`, so the client swap + request-time `RawRepresentationFactory` do not change tool registration). Record the pass count.

- [ ] **Step 5: Record the decision + finish**

Finalise `openspec/changes/model-eval-harness/report.md` with the scorecards, the think delta, and the verdict. Add a dev-flow lesson if the think-off delta is notable (a repo memory pattern). Commit:
```bash
git add Extensions/ChatExtensions.cs Features/Chat/DndChatService.cs openspec/changes/model-eval-harness/report.md
git commit -m "feat(model-eval-harness): land measured think-mode decision on the chat path"
```
(If no production change, commit just `report.md` with the KEEP-think-on finding.)

---

## Self-Review

**Spec coverage** (against tasks.md + design D1–D6):
- Task 1 ↔ tasks.md §1, design D3 (think mechanism spike). ✓ DONE — finding OVERTURNED the assumption: `think:false` API field is the only lever, needs OllamaSharp `OllamaApiClient` + `RawRepresentationFactory` (MEAI.Ollama can't; `/no_think` is a no-op here). Recorded in `report.md`.
- Task 2 ↔ tasks.md §2, design D1/D2 (console scaffold, real MEAI stack). ✓
- Task 3 ↔ tasks.md §3, design D2/D4 (stubbed tools + scenario set incl. negative + empty/fabrication cases). ✓
- Task 4 ↔ tasks.md §4, design D5 (N-run pass-rates: sel/bind/adhere + p50/p95, table + totals + config header; manual 3-scorecard bench). ✓
- Task 5 ↔ tasks.md §5, design D6 (land lever A if measured, else record; full test green). ✓
- Non-goals honoured: no shared-toolset refactor (schemas re-declared), no CI wiring, no >8GB/MoE models, local-only (lever E out of scope). ✓

**Placeholder scan:** No TBD/TODO/"handle edge cases"/"similar to Task N" — every code step carries complete code; every command has expected output. ✓

**Type consistency:** `EvalArgs`, `ModelClientFactory.{Build,BuildOptions}`, `StubState.{InvokedTools,AskRulesReturnsEmpty,Reset}`, `StubTools.Build()`, `Scenario(Name,Prompt,ExpectedTool,StubEmpty,AdherenceCheck)`, `Scenarios.All`, `RunResult(SelectedTool,BindOk,Adhered,WallMs,FirstToolCallMs)`, `ScenarioRunner.RunOnceAsync`, `Scorecard.Print` — names/signatures consistent across Tasks 2→4. `ModelClientFactory.BuildOptions(bool, IList<AITool>?)` (Task 2) is the method used in Task 4's runner. Production landing (Task 5) uses OllamaSharp `OllamaApiClient` + `ChatOptions.RawRepresentationFactory` → `ChatRequest.Think`. ✓

**Known risk carried from design:** re-declared stub schemas can drift from `DndChatService`; accepted for v1 (dev tool, not a correctness gate). The stubs copy production signatures verbatim as of this plan — if a production tool signature changes later, the corresponding stub must be updated by hand.
