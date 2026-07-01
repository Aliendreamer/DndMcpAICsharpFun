## Why

The repo has grown to ~28k lines of C# across 12 feature slices plus infra (Docker, dev/prod compose, CPM build chain) through many fast-moving extraction/companion cycles, and it is heading toward an internet-exposed VPS deployment. No holistic pass has ever checked the whole surface for security posture, structural drift, .NET best-practice violations, dead weight, or latent correctness bugs — findings so far have been incidental to feature work.

## What Changes

- Run a **findings-only full-repository audit** across five dimensions: security, structure/architecture, .NET best practices, simplification/dead code, and correctness (including tests and infra).
- Method (approved in brainstorm, option A): an **automated tooling first pass** (NuGet vulnerable-package scan, analyzer/format checks, scripted config/secret-pattern scan), then **parallel read-only dimension audit agents** over the code, then an **adversarial verification pass** that attempts to refute every Critical/Important finding before it enters the report.
- Deliverable: a triaged, markdownlint-clean report at `docs/audits/2026-07-02-full-repo-audit.md`. Each finding carries: ID, severity (Critical/Important/Minor), dimension, `file:line`, what/why, concrete fix recommendation, effort estimate (S/M/L).
- **No code changes in this change.** Fixes are decided together with the user afterwards, in triage, and become separate follow-up changes.
- Security findings are judged against an **internet-exposed VPS** threat model as primary, with local-dev-only context noted where it lowers severity.
- Out of scope for reading: `books/` data, canonical JSON content, `openspec/` history, and the git-crypt-encrypted `Config/appsettings*.json` / `.env` contents (secret *handling patterns* are audited; secret *values* are never read).

## Capabilities

### New Capabilities

- `full-repo-audit`: a one-shot audited snapshot of the repository — coverage guarantees (every feature slice, Program.cs, both compose files, Dockerfile, build props, tests, tools visited), a verification gate for high-severity findings, and a triage-ready report format.

### Modified Capabilities

_None — this change produces a report; no existing runtime capability's requirements change._

## Impact

- New file: `docs/audits/2026-07-02-full-repo-audit.md` (must pass `pnpm lint:md`).
- Read-only over: `Features/`, `Domain/`, `Infrastructure/`, `CompanionUI/`, `Program.cs`, `DndMcpAICsharpFun.Tests/`, `Tools/SchemaGenerator/`, `Dockerfile`, `docker-compose.yml`, `docker-compose.prod.yml`, `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, `DndMcpAICsharpFun.http`, `dnd-mcp-api.insomnia.json`.
- Tooling executed: `dotnet list package --vulnerable --include-transitive`, `dotnet build` (warnings-as-errors baseline), `dotnet format --verify-no-changes`.
- No runtime code, config, or API changes; no new dependencies.
