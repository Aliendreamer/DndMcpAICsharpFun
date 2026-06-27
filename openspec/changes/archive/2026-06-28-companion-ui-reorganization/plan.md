# CompanionUI Folder Reorganization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `Components/` and `wwwroot/` into `CompanionUI/` subfolder for structural clarity, with matching config updates so the build and runtime continue to work.

**Architecture:** Pure directory move within the single `DndMcpAICsharpFun` project — no new `.csproj`, no namespace changes. Two config touches are required: `<WebRoot>` in the csproj tells the SDK where to find static assets at build/publish time; `WebRootPath` in `WebApplicationOptions` tells the ASP.NET Core runtime where to serve them from.

**Tech Stack:** .NET 10, ASP.NET Core / Blazor Server, MSBuild (`Microsoft.NET.Sdk.Web`)

## Global Constraints

- No namespace changes — components remain in `DndMcpAICsharpFun.Components.*`
- No new project files
- `dotnet build` must produce zero errors after changes
- `Extensions/BlazorExtensions.cs` and all `Features/` code are untouched

---

### Task 1: Move UI folders

**Files:**
- Move: `Components/` → `CompanionUI/Components/`
- Move: `wwwroot/` → `CompanionUI/wwwroot/`

- [ ] **Step 1.1: Create the CompanionUI directory and move folders**

```bash
mkdir -p CompanionUI
git mv Components CompanionUI/Components
git mv wwwroot CompanionUI/wwwroot
```

- [ ] **Step 1.2: Verify the new layout**

```bash
ls CompanionUI/
# Expected: Components  wwwroot
ls CompanionUI/Components/
# Expected: App.razor  Layout  Pages  Routes.razor  _Imports.razor
ls CompanionUI/wwwroot/
# Expected: app.css  app.js
```

- [ ] **Step 1.3: Confirm old folders are gone**

```bash
ls -d Components wwwroot 2>&1
# Expected: ls: cannot access 'Components': No such file or directory
#           ls: cannot access 'wwwroot': No such file or directory
```

---

### Task 2: Update configuration and clean up

**Files:**
- Modify: `DndMcpAICsharpFun.csproj` — add `<WebRoot>` property
- Modify: `Program.cs:14` — switch to `WebApplicationOptions`
- Delete: `DndMcpAICompanion/` (bin/obj only, no source)

**Interfaces:**
- Consumes: moved folders from Task 1
- Produces: working build with static files served from `CompanionUI/wwwroot/`

- [ ] **Step 2.1: Add `<WebRoot>` to csproj**

In `DndMcpAICsharpFun.csproj`, add `<WebRoot>` inside the existing `<PropertyGroup>`:

```xml
<PropertyGroup>
  <WebRoot>CompanionUI\wwwroot</WebRoot>
  <DefaultItemExcludes>$(DefaultItemExcludes);DndMcpAICsharpFun.Tests/**;DndMcpAICompanion/**;DndMcpAICompanion.Tests/**;.worktrees/**;Tools/**</DefaultItemExcludes>
</PropertyGroup>
```

- [ ] **Step 2.2: Update `Program.cs` to set `WebRootPath`**

Line 14 of `Program.cs` currently reads:

```csharp
var builder = WebApplication.CreateBuilder(args);
```

Replace with:

```csharp
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = "CompanionUI/wwwroot",
});
```

- [ ] **Step 2.3: Delete the orphaned `DndMcpAICompanion/` folder**

```bash
rm -rf DndMcpAICompanion
```

- [ ] **Step 2.4: Run `dotnet build` — must exit 0**

```bash
dotnet build 2>&1 | tail -5
# Expected last line: Build succeeded.
#   0 Warning(s)
#   0 Error(s)
```

- [ ] **Step 2.5: Commit**

```bash
git add DndMcpAICsharpFun.csproj Program.cs
git commit -m "refactor: reorganize UI source under CompanionUI/ subfolder"
```
