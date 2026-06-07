# central-build-configuration Specification

## Purpose
TBD - created by archiving change centralize-build-configuration. Update Purpose after archive.
## Requirements
### Requirement: Common MSBuild properties are defined once at repo root
A root `Directory.Build.props` SHALL define `TargetFramework` (net10.0), `Nullable` (enable), `ImplicitUsings` (enable), `TreatWarningsAsErrors` (true), and `EnforceCodeStyleInBuild` (true) for every project in the solution. Individual csproj files SHALL NOT repeat these properties unless deliberately overriding with a justification comment.

#### Scenario: New project inherits solution defaults

- **WHEN** a new csproj is added under the repo root with no TargetFramework property
- **THEN** it builds targeting net10.0 with nullable reference types and warnings-as-errors enabled

#### Scenario: Solution builds warning-clean

- **WHEN** `dotnet build` runs on a clean checkout
- **THEN** the build succeeds with zero warnings (warnings fail the build)

### Requirement: Package versions are managed centrally
A root `Directory.Packages.props` with `ManagePackageVersionsCentrally=true` SHALL declare every NuGet package version exactly once. csproj `PackageReference` items SHALL NOT carry `Version` attributes.

#### Scenario: Single source of truth for a shared package

- **WHEN** two projects reference the same package
- **THEN** both resolve the identical version declared in `Directory.Packages.props`

#### Scenario: Restore is warning-free

- **WHEN** `dotnet restore` runs
- **THEN** no NU1603/NU1605 version-resolution warnings are emitted

### Requirement: Test projects share their test stack via an opt-in flag
`Directory.Build.targets` SHALL provide the common test packages (xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, FluentAssertions, coverlet.collector) to any project that sets `IsTestProject=true`. Test csprojs SHALL NOT declare these references individually.

#### Scenario: Test project gets the stack from the flag

- **WHEN** a csproj sets `<IsTestProject>true</IsTestProject>` and declares no xunit reference
- **THEN** its tests build and run under `dotnet test`

### Requirement: Canonical schema generation is incremental
The `GenerateCanonicalSchemas` target SHALL skip regeneration when its inputs are unchanged since the last successful generation, and SHALL regenerate when the SchemaGenerator or its inputs change. The `SkipCanonicalSchemaGen=true` escape hatch SHALL be preserved.

#### Scenario: No-op rebuild skips generation

- **WHEN** `dotnet build` runs twice in a row with no source changes
- **THEN** the second build does not invoke the SchemaGenerator

#### Scenario: Generator change triggers regeneration

- **WHEN** a SchemaGenerator source file changes and `dotnet build` runs
- **THEN** the schemas are regenerated

