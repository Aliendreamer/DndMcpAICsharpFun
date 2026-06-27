## Context

The single-host project (`DndMcpAICsharpFun`) serves API, MCP, and Blazor Server UI from one process. Blazor UI source lives in `Components/` and `wwwroot/` at the project root, interleaved with backend folders (`Domain/`, `Features/`, `Infrastructure/`, etc.). This makes it harder to navigate. The fix is a pure folder move — no new `.csproj`, no namespace changes, no runtime behaviour changes.

## Goals / Non-Goals

**Goals:**

- Group all Blazor UI source (`Components/`, `wwwroot/`) under `CompanionUI/`
- Keep the single-host architecture intact
- Delete the orphaned `DndMcpAICompanion/` folder (bin/obj only, no source)
- Build and serve static files correctly after the move

**Non-Goals:**

- No new project or solution file
- No namespace changes
- No changes to services, routes, or tests
- No changes to `Extensions/BlazorExtensions.cs`

## Decisions

**Folder name: `CompanionUI/`**
Chosen by the user. Groups UI assets without implying a separate project.

**WebRoot configuration via `WebApplicationOptions`**
`Microsoft.NET.Sdk.Web` defaults to `wwwroot/` at the project root. After moving to `CompanionUI/wwwroot/`, two things need updating:

- **Runtime**: `WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, WebRootPath = "CompanionUI/wwwroot" })` — tells ASP.NET Core static files middleware where to serve from.
- **Build/publish**: `<WebRoot>CompanionUI\wwwroot</WebRoot>` in the `.csproj` `<PropertyGroup>` — tells the SDK to glob and copy files from the new path into the output directory.

Both are required; omitting either breaks dev or publish respectively.

**Namespaces unchanged**
Razor components are discovered by assembly scanning, not file path. Moving files does not affect `DndMcpAICsharpFun.Components.*` namespaces or how the host registers them.

## Risks / Trade-offs

- **Hot reload path sensitivity** — `dotnet watch` may need a restart after the move because it tracks file paths. One-time inconvenience. → Mitigation: restart watch after move.
- **Docker COPY paths** — `Dockerfile` `COPY` instructions that reference `wwwroot/` or `Components/` must be updated. → Mitigation: check and update Dockerfile as part of the task.

## Migration Plan

1. Move directories: `Components/` → `CompanionUI/Components/`, `wwwroot/` → `CompanionUI/wwwroot/`
2. Update `.csproj`: add `<WebRoot>CompanionUI\wwwroot</WebRoot>`
3. Update `Program.cs`: use `WebApplicationOptions` with `WebRootPath`
4. Update `Dockerfile` if it references `wwwroot/` or `Components/`
5. Delete `DndMcpAICompanion/`
6. `dotnet build` — verify zero errors
7. `dotnet run` — verify UI loads and static assets are served

Rollback: `git revert` — all changes are purely structural.
