# Proposal: CompanionUI Folder Reorganization

## What

Move `Components/` and `wwwroot/` into a dedicated `CompanionUI/` subfolder within the existing `DndMcpAICsharpFun` project. No new `.csproj` is created — this is a pure directory reorganization inside the single-host project.

Also delete the leftover `DndMcpAICompanion/` folder at the repo root, which contains only `bin/`+`obj/` artifacts from the previously merged companion project and no source files.

## Why

The project root currently mixes Blazor UI concerns (`Components/`, `wwwroot/`) with backend concerns (`Domain/`, `Features/`, `Infrastructure/`, `Tools/`, `Migrations/`, etc.). A developer opening the repo cannot quickly tell what is API/MCP code and what is UI code. Grouping the UI assets under `CompanionUI/` makes the separation obvious at a glance.

## Scope

**In scope:**

- Move `Components/` → `CompanionUI/Components/`
- Move `wwwroot/` → `CompanionUI/wwwroot/`
- Update `DndMcpAICsharpFun.csproj`: add `<WebRoot>CompanionUI\wwwroot</WebRoot>`
- Update `Program.cs`: pass `WebRootPath = "CompanionUI/wwwroot"` via `WebApplicationOptions`
- Delete `DndMcpAICompanion/` (bin/obj only, already excluded from the csproj)

**Out of scope:**

- No new project file
- No namespace changes (components remain in `DndMcpAICsharpFun.Components.*`)
- No changes to `Extensions/BlazorExtensions.cs` or any `Features/` code
- No changes to routes, services, or tests
