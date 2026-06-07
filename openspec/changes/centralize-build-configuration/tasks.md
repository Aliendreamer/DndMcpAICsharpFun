# Tasks — centralize-build-configuration

## 1. Inventory + baseline

- [ ] 1.1 Record current resolved versions: `dotnet list package` per project; note every package+version pair and the NU1603 offender's actual resolved version
- [ ] 1.2 Clean build baseline: `dotnet build --nologo` — capture the full current warning list (BL0008 instances, NU1603, anything else)

## 2. Directory.Build.props

- [ ] 2.1 Create root `Directory.Build.props` (TargetFramework, Nullable, ImplicitUsings, TreatWarningsAsErrors, EnforceCodeStyleInBuild, `IsTestProject=false` default)
- [ ] 2.2 Strip the now-inherited properties from all 5 csproj files
- [ ] 2.3 Build — fix or justified-suppress (`NoWarn` + comment) every surfaced warning; BL0008 in companion Razor gets fixed properly
- [ ] 2.4 Full test suite green; commit

## 3. Directory.Packages.props (CPM)

- [ ] 3.1 Create `Directory.Packages.props` with `ManagePackageVersionsCentrally=true`; declare every package once in labeled ItemGroups (versions from 1.1 inventory — current-resolved, no upgrades)
- [ ] 3.2 Remove `Version` attributes from all `PackageReference` items in the 5 csprojs
- [ ] 3.3 Fix the Microsoft.Extensions.AI preview pin so `dotnet restore` is NU1603-free
- [ ] 3.4 `dotnet restore` warning-free + build clean + full suite green; commit

## 4. Directory.Build.targets (test stack)

- [ ] 4.1 Create `Directory.Build.targets` with the `IsTestProject` conditional group (xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, FluentAssertions, coverlet.collector)
- [ ] 4.2 Both test csprojs: set `IsTestProject=true`, delete the now-shared references (keep project-specific ones like Microsoft.Data.Sqlite per design)
- [ ] 4.3 `dotnet test` — both projects discovered and green; commit

## 5. Incremental schema generation

- [ ] 5.1 Add `Inputs`/`Outputs` (or stamp-file fallback per design decision 5) to `Build/GenerateCanonicalSchemas.targets`
- [ ] 5.2 Verify: second consecutive `dotnet build` skips the generator (build log shows target skipped); touching a SchemaGenerator source re-triggers it
- [ ] 5.3 Schemas regenerate byte-identical (git diff clean after a forced regen); commit

## 6. Verification + docs

- [ ] 6.1 Clean-slate verification: remove all bin/obj, `dotnet build`, `dotnet test` — 487+ green, zero warnings
- [ ] 6.2 Update CLAUDE.md (key project settings section: mention central props/CPM); README if it documents build specifics
- [ ] 6.3 Commit
