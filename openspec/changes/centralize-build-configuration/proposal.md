# Centralize Build Configuration

## Why

Five csproj files (`DndMcpAICsharpFun`, `DndMcpAICompanion`, two test projects, `Tools/SchemaGenerator`) each repeat common properties and pin their own package versions. Seven packages are pinned in two places (xunit, xunit.runner, NJsonSchema, Test.Sdk, Microsoft.Extensions.AI.Ollama, Microsoft.Data.Sqlite, FluentAssertions); `TreatWarningsAsErrors` is enforced in only one project; the long-standing NU1603 warning (Microsoft.Extensions.AI preview pin resolving to a different version in the companion) is a direct symptom of decentralized versions. The sibling project (aidoctor) already runs the target pattern — `Directory.Build.props` + `Directory.Build.targets` + `Directory.Packages.props` (Central Package Management) — and it works well there.

## What Changes

- New `Directory.Build.props` at repo root: `TargetFramework=net10.0`, `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true` — removed from every csproj.
- New `Directory.Packages.props` with `ManagePackageVersionsCentrally=true`: every `PackageVersion` declared once, grouped by label (AI/Ollama, OpenTelemetry, Serilog, Qdrant, PDF, Testing, …); all `PackageReference` entries in csproj files lose their `Version` attributes.
- The NU1603 Microsoft.Extensions.AI preview pin is resolved while consolidating (align to one resolvable version across app + companion).
- New `Directory.Build.targets` for shared conditional package groups if duplication warrants it (e.g. test projects: xunit + FluentAssertions + Test.Sdk + coverlet behind an `IsTestProject` flag) — optional, only where it removes real duplication.
- Existing `Build/GenerateCanonicalSchemas.targets` stays (still imported explicitly by the main csproj); gains MSBuild `Inputs`/`Outputs` so schema regeneration is incremental instead of running `dotnet run` on every build.
- **BREAKING (internal): turning on `TreatWarningsAsErrors` solution-wide** — any existing warnings (e.g. BL0008 in companion razor files) must be fixed or explicitly suppressed with justification as part of this change.

## Capabilities

### New Capabilities

- `central-build-configuration`: One root-level definition of common MSBuild properties and all NuGet package versions (CPM); per-project csproj files contain only project-specific settings and version-less package references.

### Modified Capabilities

- (none — build infrastructure only; no runtime behavior changes)

## Impact

- New: `Directory.Build.props`, `Directory.Packages.props`, optional `Directory.Build.targets` (repo root)
- Modified: all 5 `.csproj` files (strip duplicated properties + Version attributes), `Build/GenerateCanonicalSchemas.targets` (incremental Inputs/Outputs)
- Possible source fixes wherever `TreatWarningsAsErrors` surfaces existing warnings (companion BL0008, NU1603)
- CI/dev workflow unchanged: `dotnet build` / `dotnet test` as before
