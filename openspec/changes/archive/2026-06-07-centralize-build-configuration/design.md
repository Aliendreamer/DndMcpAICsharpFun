# Centralize Build Configuration — Design

## Context

The solution (`DndMcpAICsharpFun.slnx`) holds 5 projects with copy-pasted `<TargetFramework>`, `<Nullable>`, `<ImplicitUsings>` and per-project package versions. aidoctor's proven layout is the reference: minimal `Directory.Build.props` (common properties + an opt-in flag), `Directory.Build.targets` (conditional shared package groups), `Directory.Packages.props` (CPM with labeled ItemGroups). One custom target already exists at `Build/GenerateCanonicalSchemas.targets`, imported explicitly by the main csproj, running the SchemaGenerator before every build.

## Goals / Non-Goals

**Goals:**

1. One place for common MSBuild properties; one place for every package version.
2. Solution-wide `TreatWarningsAsErrors=true` + `EnforceCodeStyleInBuild=true` (aidoctor parity) with all surfaced warnings fixed or explicitly justified-suppressed.
3. Resolve the NU1603 Microsoft.Extensions.AI preview-pin mismatch during consolidation.
4. Make schema generation incremental (`Inputs`/`Outputs` on the existing target) so routine builds skip the nested `dotnet run`.
5. csproj files shrink to project-specific content only.

**Non-Goals:**

- No package upgrades beyond what consolidation forces (consolidate to currently-resolved versions; upgrades are separate work).
- No project restructuring, no slnx changes, no CI changes.
- Not moving `Build/GenerateCanonicalSchemas.targets` into `Directory.Build.targets` (it is main-project-specific; a global import would run it for all 5 projects).

## Decisions

1. **`Directory.Build.props` content** (mirrors aidoctor): `TargetFramework`, `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, plus `IsTestProject=false` default flag.
2. **`Directory.Build.targets`**: one conditional group `Condition="'$(IsTestProject)' == 'true'"` carrying xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, FluentAssertions, coverlet.collector. Test csprojs set `IsTestProject=true` and drop those references entirely. (Companion tests also use Microsoft.Data.Sqlite — that stays a per-project reference since the main tests need it too; it goes in the group only if both test projects use it.)
3. **CPM via `Directory.Packages.props`** with `ManagePackageVersionsCentrally=true`; labeled ItemGroups copying aidoctor's organization. Preview-pinned packages (`Microsoft.Extensions.AI.Ollama 9.7.0-preview.*`, `ModelContextProtocol.*` if preview) are pinned to versions that restore cleanly (kill NU1603 — align `Microsoft.Extensions.AI` transitives by pinning the exact resolvable version or adding a direct top-level `PackageVersion`).
4. **TreatWarningsAsErrors rollout**: fix surfaced warnings rather than blanket-suppress; known offenders: BL0008 (companion Razor), NU1603 (fixed by decision 3). Per-warning `NoWarn` only with an inline comment justifying it.
5. **Schema target incrementality**: `Inputs` = SchemaGenerator sources (`Tools/SchemaGenerator/**/*.cs`, csproj) + any schema-affecting domain sources it reflects over; `Outputs` = `Schemas/canonical/*.schema.json`. If the precise input set is hard to pin (generator reflects over the main assembly), fall back to a stamp file written after generation, keyed on the generator project's build output. The `SkipCanonicalSchemaGen` escape hatch stays.
6. **Verification gate**: `dotnet build` from clean (`git clean -xdf bin obj` equivalent) + full `dotnet test` (487 green) + `dotnet restore` warning-free.

## Risks / Trade-offs

- **TreatWarningsAsErrors may surface more warnings than the two known ones** — fix-forward; the change isn't mergeable until the build is warning-clean, which is the point.
- **CPM transitive pinning surprises** (NU1109/NU1605 class) — consolidate to currently-resolved versions to minimize movement; verify with `dotnet list package --include-transitive` spot-checks.
- **Incremental schema gen could skip a needed regen** if Inputs are under-specified — mitigated by the stamp-file fallback keyed on generator build output and the existing escape hatch (`SkipCanonicalSchemaGen=true` inverse: delete stamp to force).
