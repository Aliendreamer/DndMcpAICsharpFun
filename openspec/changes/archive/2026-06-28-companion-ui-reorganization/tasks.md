## 1. Move UI Folders

- [ ] 1.1 Create `CompanionUI/` directory and move `Components/` into it: `mv Components CompanionUI/Components`
- [ ] 1.2 Move `wwwroot/` into it: `mv wwwroot CompanionUI/wwwroot`

## 2. Update Project Configuration

- [ ] 2.1 Add `<WebRoot>CompanionUI\wwwroot</WebRoot>` to the `<PropertyGroup>` in `DndMcpAICsharpFun.csproj`
- [ ] 2.2 Update `Program.cs`: replace `WebApplication.CreateBuilder(args)` with `WebApplication.CreateBuilder(new WebApplicationOptions { Args = args, WebRootPath = "CompanionUI/wwwroot" })`

## 3. Cleanup

- [ ] 3.1 Delete `DndMcpAICompanion/` folder (bin/obj only, no source)

## 4. Verify

- [ ] 4.1 Run `dotnet build` — must exit 0 with zero errors
- [ ] 4.2 Run `dotnet run` and open the UI in a browser — verify styles load and chat/heroes/campaigns pages render correctly
