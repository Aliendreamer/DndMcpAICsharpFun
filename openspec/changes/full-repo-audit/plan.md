# Full-Repo Audit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Produce a verified, triage-ready findings report (`docs/audits/2026-07-02-full-repo-audit.md`) covering security, structure, .NET best practices, simplification, and correctness across the whole repository — with zero code changes.

**Architecture:** Three-stage pipeline run by the main session as orchestrator: (1) automated tooling evidence via bash, (2) parallel read-only dimension audit agents fed deterministic file manifests and returning JSON findings + visited ledgers, (3) adversarial verification agents that try to refute every Critical/Important finding, then synthesis into the report. This plan's "tasks" are audit stages, not code+TDD units — each stage ends with a verifiable gate instead of a test run.

**Tech Stack:** Bash tooling (`dotnet`, `grep`, `git`), Claude `Agent` tool with the read-only `Explore` agent type for all audit/verify agents, Serena MCP for symbol-level code reading inside agents, `pnpm lint:md` for the report gate.

## Global Constraints

- **No code changes.** Only writes allowed: `docs/audits/2026-07-02-full-repo-audit.md`, files under `openspec/changes/full-repo-audit/`, and scratchpad files.
- **Never read** `Config/appsettings*.json`, `.env*`, `books/` content, canonical JSON content, `node_modules/`, `obj/`, `bin/`. Secrets referenced by variable/key NAME only.
- **Threat model:** internet-exposed VPS primary; findings that only matter there are tagged `vpsOnly: true` → rendered `[vps]`.
- **Severity:** Critical = exploitable security flaw (VPS model) or data-loss/corruption bug. Important = likely bug, significant security weakening, or structural problem taxing development. Minor = style/simplification/low-risk drift.
- **Finding ID prefixes:** `SEC-`, `STR-`, `NET-`, `SIM-`, `COR-` (two-digit numbering per dimension, e.g. `SEC-01`).
- **All audit/verify agents are `Explore` type** (read-only tool set) and receive the CRITICAL-Serena block (below) in their prompt.
- Report must pass `pnpm lint:md` with 0 errors and record the audited commit SHA.

### CRITICAL-Serena block (paste into every agent prompt verbatim)

```text
CRITICAL: You have Serena MCP tools. Before reading any .cs file, call
mcp__plugin_serena_serena__initial_instructions once. Prefer
get_symbols_overview / find_symbol / find_referencing_symbols to locate and
read code symbol-by-symbol; use Serena read_file for whole-file reads of
.cs files. Never use built-in Read on .cs files. Non-code files (.yml,
.json, .props, .razor, Dockerfile, .http) may be read with built-in Read.
You are READ-ONLY: never edit, write, or run state-changing commands.
NEVER read Config/appsettings*.json, .env*, books/ content, node_modules/.
```

### Finding JSON schema (all agents return this shape)

```json
{
  "findings": [
    {
      "dimension": "security|structure|dotnet|simplification|correctness",
      "severity": "Critical|Important|Minor",
      "file": "Features/Auth/AuthEndpoints.cs",
      "line": 42,
      "title": "one-line defect statement",
      "what": "what is wrong, concretely",
      "why": "impact / failure scenario",
      "recommendation": "concrete fix, 1-3 sentences",
      "effort": "S|M|L",
      "vpsOnly": false
    }
  ],
  "ledger": {
    "visited": ["path", "..."],
    "skipped": [{ "path": "path", "reason": "why" }]
  }
}
```

---

### Task 1: Tooling pass + coverage manifest

**Files:**
- Create: `/tmp/claude-1000/-home-aliendreamer-projects-DndMcpAICsharpFun/66d7a2d9-9617-4a43-adbd-a5c5d7d085dc/scratchpad/audit/manifest-<dimension>.txt` (5 files)
- Create: `.../scratchpad/audit/evidence.md` (tooling output digest)

**Interfaces:**
- Produces: per-dimension manifest files (one path per line) and `evidence.md`, consumed verbatim by Task 2 agent prompts; `AUDIT_SHA` recorded at the top of `evidence.md`.

- [ ] **Step 1: Record SHA and generate the master file list**

```bash
SCRATCH=/tmp/claude-1000/-home-aliendreamer-projects-DndMcpAICsharpFun/66d7a2d9-9617-4a43-adbd-a5c5d7d085dc/scratchpad/audit
mkdir -p "$SCRATCH"
git rev-parse HEAD | tee "$SCRATCH/sha.txt"
{ find Features Domain Infrastructure CompanionUI -type f \( -name '*.cs' -o -name '*.razor' -o -name '*.razor.cs' \) ! -path '*/obj/*' ! -path '*/bin/*'
  echo Program.cs
  find DndMcpAICsharpFun.Tests Tools -type f -name '*.cs' ! -path '*/obj/*' ! -path '*/bin/*'
  echo Dockerfile; echo docker-compose.yml; echo docker-compose.prod.yml
  echo Directory.Build.props; echo Directory.Build.targets; echo Directory.Packages.props
  echo DndMcpAICsharpFun.http; echo dnd-mcp-api.insomnia.json
} | sort -u > "$SCRATCH/manifest-all.txt"
wc -l "$SCRATCH/manifest-all.txt"
```

Expected: ~380 lines.

- [ ] **Step 2: Partition into per-dimension mandatory manifests**

Ownership rule (a path may appear in several manifests; the UNION must equal `manifest-all.txt` — verified in Step 3):

| Dimension | Mandatory ownership |
|---|---|
| security | `Program.cs`, `Features/Auth/**`, `Features/Admin/**`, `Features/Mcp/**`, `Features/Chat/**`, `CompanionUI/**`, `Dockerfile`, both composes, `.http`, insomnia |
| structure | `Domain/**`, `Infrastructure/**`, `Program.cs`, one representative pass over every `Features/*` slice (all `.cs` files listed) |
| dotnet | `Infrastructure/**`, `Features/Ingestion/**`, `Features/Embedding/**`, `Features/VectorStore/**`, `Features/Retrieval/**`, `Features/Search/**`, `Features/Resolution/**`, `Features/Entities/**`, `Features/Campaigns/**` |
| simplification | `Features/**`, `Tools/**`, `Directory.Packages.props`, `Directory.Build.props`, `Directory.Build.targets` |
| correctness | `DndMcpAICsharpFun.Tests/**`, `Features/Ingestion/**`, `Features/Entities/**`, `Features/Resolution/**`, `Tools/**`, `Dockerfile`, both composes, `Directory.Build.*`, `.http`, insomnia |

```bash
grep -E '^(Program\.cs|Features/(Auth|Admin|Mcp|Chat)/|CompanionUI/|Dockerfile|docker-compose|DndMcpAICsharpFun\.http|dnd-mcp-api)' "$SCRATCH/manifest-all.txt" > "$SCRATCH/manifest-security.txt"
grep -E '^(Domain/|Infrastructure/|Program\.cs|Features/)' "$SCRATCH/manifest-all.txt" > "$SCRATCH/manifest-structure.txt"
grep -E '^(Infrastructure/|Features/(Ingestion|Embedding|VectorStore|Retrieval|Search|Resolution|Entities|Campaigns)/)' "$SCRATCH/manifest-all.txt" > "$SCRATCH/manifest-dotnet.txt"
grep -E '^(Features/|Tools/|Directory\.)' "$SCRATCH/manifest-all.txt" > "$SCRATCH/manifest-simplification.txt"
grep -E '^(DndMcpAICsharpFun\.Tests/|Features/(Ingestion|Entities|Resolution)/|Tools/|Dockerfile|docker-compose|Directory\.|DndMcpAICsharpFun\.http|dnd-mcp-api)' "$SCRATCH/manifest-all.txt" > "$SCRATCH/manifest-correctness.txt"
```

- [ ] **Step 3: Verify the union covers everything**

```bash
sort -u "$SCRATCH"/manifest-{security,structure,dotnet,simplification,correctness}.txt | diff - "$SCRATCH/manifest-all.txt"
```

Expected: empty diff. If not, extend a dimension's grep until it is.

- [ ] **Step 4: Run automated tooling, capture into evidence.md**

```bash
dotnet restore
dotnet list package --vulnerable --include-transitive 2>&1 | tee "$SCRATCH/vuln.txt"
dotnet build 2>&1 | tail -5 | tee "$SCRATCH/build.txt"
dotnet format --verify-no-changes 2>&1 | tail -5 | tee "$SCRATCH/format.txt"
```

Expected: build 0 warnings/errors (warnings-as-errors); note any format drift.

- [ ] **Step 5: Scripted scans (endpoint inventory, anonymous markers, secret patterns)**

```bash
grep -rnE '\.Map(Get|Post|Put|Delete)\(' --include='*.cs' Features/ Program.cs CompanionUI/ | tee "$SCRATCH/endpoints.txt"
grep -rniE 'AllowAnonymous|RequireAuthorization|AddAuthorization|UseAuthentication' --include='*.cs' Features/ Program.cs | tee "$SCRATCH/authmarkers.txt"
grep -rniE '(password|secret|apikey|api_key|token)\s*[:=]\s*"[^"{]' --include='*.cs' --include='*.yml' --include='*.props' . 2>/dev/null | grep -v -E '(Tests/|obj/|bin/|node_modules/)' | tee "$SCRATCH/secretscan.txt"
```

Assemble `evidence.md` in the scratchpad: SHA, vuln scan result, build/format baselines, endpoint inventory (count + list), auth markers, secret-scan hits (values redacted — pattern + location only).

- [ ] **Step 6: Mark tasks 1.1–1.4 done in tasks.md and commit tasks.md progress**

```bash
git add openspec/changes/full-repo-audit/tasks.md
git commit -m "docs(openspec): full-repo-audit task group 1 done (tooling evidence)"
```

---

### Task 2: Parallel dimension audits (agent fan-out)

**Files:**
- Create: `.../scratchpad/audit/findings-<dimension>[-<chunk>].json` (one per agent)

**Interfaces:**
- Consumes: `manifest-<dimension>.txt`, `evidence.md` from Task 1.
- Produces: findings JSON files (schema above) consumed by Task 3; union of ledgers must cover `manifest-all.txt`.

- [ ] **Step 1: Chunk any manifest over 60 files**

Split with `split -l 60` into `manifest-<dimension>-a.txt`, `-b.txt`, … Each chunk gets its own agent (keeps per-agent scope readable end-to-end).

- [ ] **Step 2: Dispatch one Explore agent per dimension-chunk, all in parallel**

Prompt template (fill `<DIMENSION CHARTER>`, `<FILE LIST>`, paste evidence digest):

```text
You are a read-only <dimension> auditor for a .NET 10 single-host app
(API + MCP server + Blazor Server UI; EF Core/Postgres; Qdrant; Ollama).
Threat model for security judgments: internet-exposed VPS.

<CRITICAL-Serena block>

CHARTER: <DIMENSION CHARTER — see list below>

Audit EVERY file in this list; do not sample:
<FILE LIST from manifest chunk>

Tooling evidence gathered earlier (build/format/vuln/endpoints/auth markers):
<evidence.md digest>

Return ONLY a JSON object matching this schema (no prose):
<Finding JSON schema>

Rules: cite exact file + line for every finding; severity per: Critical =
exploitable security flaw (VPS) or data-loss/corruption; Important = likely
bug, significant security weakening, or structural problem taxing
development; Minor = style/simplification/low-risk drift. Set vpsOnly=true
for findings that only matter when internet-exposed. List EVERY file from
the list in ledger.visited or ledger.skipped (with reason). Report real
defects, not preferences; if a file is clean, it is simply visited.
```

Dimension charters:
- **security:** endpoint authn/z (classify every endpoint: anonymous / user-auth / admin / MCP-key), admin surface exposure, MCP `Mcp:ApiKey` handling, cookie/session flags, rate limiting coverage, secrets hygiene (names only), SSRF/path-traversal in ingestion (PDF upload, Marker/MinerU/Ollama/5etools calls, file paths from user input), CORS/headers, Blazor auth circuits, Docker/compose exposure (ports, privileged, mounts), unauthenticated `/metrics`.
- **structure:** feature-slice boundaries (cross-slice references), Domain purity (Domain referencing infrastructure), dependency direction, God files (>500 lines flag, note responsibility split), Program.cs composition-root sprawl, repository/DbContextFactory pattern consistency.
- **dotnet:** async correctness (async-void, `.Result`/`.Wait()`, missing ConfigureAwait not required in ASP.NET but flag sync-over-async), CancellationToken propagation on I/O paths, DI lifetime mismatches (singleton capturing scoped), EF Core (N+1, tracking vs no-tracking, missing indexes hinted by queries, JSON column usage), IDisposable/IAsyncDisposable handling, HttpClient usage (factory vs new), nullability suppressions (`!`), exception swallowing.
- **simplification:** dead code (unreferenced symbols — use find_referencing_symbols), duplicated logic across slices, over-abstraction (interfaces with one implementation used once), unused NuGet packages in Directory.Packages.props, leftover migration/compat shims, commented-out code blocks.
- **correctness:** logic bugs, race conditions (lazy init, shared state in singletons, Blazor circuit state), error-path behavior (partial writes, checkpoint resume), test smells (assertion-free tests, over-mocking, Testcontainers misuse), coverage gaps on auth/ingestion/extraction critical paths (name specific untested behaviors), Dockerfile/compose hygiene (layer caching, root user, healthchecks), `.http`/insomnia drift vs registered endpoints (use endpoints.txt).

Save each agent's JSON to `findings-<dimension>[-<chunk>].json`.

- [ ] **Step 3: Reconcile ledgers against manifest-all**

Concatenate all `ledger.visited` arrays; `diff` sorted union against `manifest-all.txt`. For any gap or skipped path, dispatch a follow-up Explore agent with just those paths (same template). Repeat until empty.

- [ ] **Step 4: Mark tasks 2.1–2.6 done in tasks.md, commit progress**

```bash
git add openspec/changes/full-repo-audit/tasks.md
git commit -m "docs(openspec): full-repo-audit task group 2 done (dimension audits + coverage reconciled)"
```

---

### Task 3: Merge, dedup, adversarial verification

**Files:**
- Create: `.../scratchpad/audit/merged.json` (deduped, IDs assigned)
- Create: `.../scratchpad/audit/verdicts.json`

**Interfaces:**
- Consumes: all `findings-*.json`.
- Produces: `merged.json` where every Critical/Important finding has `verdict: "CONFIRMED" | "REFUTED" | "DOWNGRADED"` and refutation notes; consumed by Task 4.

- [ ] **Step 1: Merge and dedup**

Load all findings JSON; dedup by `(file, line±3)` key keeping the highest-severity copy and merging `what`/`why` text; assign IDs per dimension in file order (`SEC-01`, `SEC-02`, …). Write `merged.json`.

- [ ] **Step 2: Dispatch refute agents for every Critical/Important finding**

Batch up to 5 findings per Explore agent (same-dimension batches). Prompt:

```text
You are a skeptical verifier. For EACH finding below, try to REFUTE it by
re-reading the cited code and its callers/guards. Default to REFUTED if
the claimed behavior cannot actually occur.

<CRITICAL-Serena block>

Findings:
<JSON array of findings with id/file/line/what/why>

Return ONLY JSON:
{ "verdicts": [ { "id": "SEC-01",
    "verdict": "CONFIRMED|REFUTED|DOWNGRADED",
    "reason": "what you re-read and why it holds or fails" } ] }
DOWNGRADED means: real but overstated — it is Minor, say why.
```

- [ ] **Step 3: Apply verdicts**

REFUTED → drop from report, keep in a synthesis appendix note. DOWNGRADED → set severity Minor, keep verifier reason. CONFIRMED → mark verified. Spot-check 10 random Minor findings yourself (read the cited lines via Serena); drop any that misread code.

- [ ] **Step 4: Mark tasks 3.1–3.3 done in tasks.md, commit progress**

```bash
git add openspec/changes/full-repo-audit/tasks.md
git commit -m "docs(openspec): full-repo-audit task group 3 done (findings verified)"
```

---

### Task 4: Report, lint, finish

**Files:**
- Create: `docs/audits/2026-07-02-full-repo-audit.md`
- Modify: `openspec/changes/full-repo-audit/tasks.md` (final checkboxes)

**Interfaces:**
- Consumes: `merged.json` (verified), `evidence.md`.
- Produces: the committed report — the change's deliverable.

- [ ] **Step 1: Write the report** with this skeleton:

```markdown
# Full Repository Audit — 2026-07-02

Audited commit: `<AUDIT_SHA>` · Threat model: internet-exposed VPS (`[vps]` = exposure-only)

## Summary

<counts by severity and dimension; top 5 headline findings; one-paragraph overall posture>

## Tooling evidence

<vuln scan result, build/format baseline, endpoint count>

## Endpoint auth posture

| Endpoint | Method | Classification | Notes |
| --- | --- | --- | --- |

## Critical findings

### SEC-01 — <title>
- **Where:** `file:line` · **Dimension:** security · **Effort:** M · **Verified:** yes
- **What:** …
- **Why it matters:** …
- **Recommendation:** …

## Important findings
<same format>

## Minor findings
<compact table per dimension: ID · file:line · title · recommendation · effort>

## Coverage

<manifest size, files visited, re-dispatch count; explicit statement that union ledger == manifest>
```

- [ ] **Step 2: Lint**

```bash
pnpm lint:md:fix && pnpm lint:md
```

Expected: `0 error(s)`.

- [ ] **Step 3: Verify no stray changes and commit**

```bash
git status --short   # only docs/audits/ + openspec/changes/full-repo-audit/ (+ known sandbox mask noise)
git add docs/audits/2026-07-02-full-repo-audit.md openspec/changes/full-repo-audit/tasks.md
git commit -m "docs(audit): full-repo audit report — verified findings, endpoint auth posture, coverage proven"
```

- [ ] **Step 4: Hand off to joint triage** — present the summary + finding IDs to the user; fix batches become follow-up openspec changes (task 4.4 closes there).
