## ADDED Requirements

### Requirement: Audit coverage is complete and proven

The audit SHALL visit every in-scope surface — all `Features/` slices, `Domain/`, `Infrastructure/`, `CompanionUI/`, `Program.cs`, `DndMcpAICsharpFun.Tests/`, `Tools/SchemaGenerator/`, `Dockerfile`, `docker-compose.yml`, `docker-compose.prod.yml`, `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, `DndMcpAICsharpFun.http`, `dnd-mcp-api.insomnia.json` — and SHALL prove coverage via per-path visited ledgers reconciled against a generated file manifest before the report is written.

#### Scenario: Coverage ledger reconciliation

- **WHEN** all dimension audit passes have returned their visited/skipped ledgers
- **THEN** every manifest path is marked visited by at least one dimension, and any skipped path is re-dispatched until the manifest is fully covered

#### Scenario: Out-of-scope paths are never read

- **WHEN** an audit pass encounters `Config/appsettings*.json`, `.env*`, `books/` data, canonical JSON content, or `node_modules/`
- **THEN** the path is excluded from reading; secret-bearing config is discussed in findings only by reference (variable/key name), never by value

### Requirement: Five audit dimensions are each applied

The audit SHALL evaluate the codebase on five dimensions: security (primary threat model: internet-exposed VPS), structure/architecture, .NET best practices, simplification/dead code, and correctness including test quality and infra hygiene.

#### Scenario: Dimension passes run against the manifest

- **WHEN** the audit executes
- **THEN** each of the five dimensions produces its own findings list over its manifest slice, independent of the other dimensions

#### Scenario: Endpoint auth posture classification

- **WHEN** the security dimension completes
- **THEN** the report contains a table classifying every registered HTTP endpoint (`MapGet`/`MapPost`/`MapPut`/`MapDelete`) and the MCP surface as anonymous, user-auth, admin, or MCP-key protected, with no endpoint missing

### Requirement: Automated tooling evidence precedes manual review

The audit SHALL first run automated checks — `dotnet list package --vulnerable --include-transitive`, a `dotnet build` warning baseline, `dotnet format --verify-no-changes`, and scripted scans for secret-pattern leaks and anonymous-endpoint markers — and SHALL feed their output to the manual dimension passes.

#### Scenario: Vulnerable package scan result recorded

- **WHEN** the NuGet vulnerability scan completes
- **THEN** the report records the scan output (vulnerable packages with advisories, or an explicit clean result) with the scan date and lockfile state

### Requirement: Critical and Important findings are adversarially verified

Every finding triaged Critical or Important SHALL be independently verified by a pass that attempts to refute it against live code before publication; refuted findings SHALL be dropped or downgraded with the refutation reasoning retained.

#### Scenario: Finding survives verification

- **WHEN** a verifier re-reads the cited `file:line` and cannot refute the finding
- **THEN** the finding enters the report marked verified

#### Scenario: Finding is refuted

- **WHEN** a verifier demonstrates the cited behavior cannot occur (guard elsewhere, dead path, misread code)
- **THEN** the finding is removed or downgraded to Minor, and the refutation note is kept in the synthesis record

### Requirement: Report is triage-ready and lint-clean

The audit SHALL produce `docs/audits/2026-07-02-full-repo-audit.md` in which every finding has a stable ID (`SEC-`, `STR-`, `NET-`, `SIM-`, `COR-` prefix), severity (Critical/Important/Minor), dimension, `file:line` citation, a what/why explanation, a concrete fix recommendation, and an S/M/L effort estimate; the file SHALL pass `pnpm lint:md` with zero errors and SHALL record the audited commit SHA.

#### Scenario: Report lints clean

- **WHEN** the report file is finalized
- **THEN** `pnpm lint:md` reports 0 errors for it

#### Scenario: Findings are individually addressable in triage

- **WHEN** the user and assistant triage the report together
- **THEN** any subset of findings can be referenced unambiguously by ID to form a follow-up fix change

### Requirement: The audit changes no code

The audit SHALL make no modifications to runtime code, configuration, dependencies, or API contracts; its only repository writes are the report file and the openspec change artifacts.

#### Scenario: Clean working tree apart from deliverables

- **WHEN** the audit completes
- **THEN** `git status` shows changes only under `docs/audits/` and `openspec/changes/full-repo-audit/` (modulo known sandbox mask noise)
