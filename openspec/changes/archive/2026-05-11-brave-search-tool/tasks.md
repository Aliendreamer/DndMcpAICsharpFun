## 1. Docker Compose — SearXNG Service

- [x] 1.1 Add `searxng` service to `docker-compose.yml` using `searxng/searxng:latest` image on port `8888:8080`, connected to `dnd_net`
- [x] 1.2 Mount a minimal `./infra/searxng/settings.yml` config file enabling JSON format output and disabling rate limiting for local use

## 2. Configuration

- [x] 2.1 Add `SearXNG` section to `Config/appsettings.json` with `Url: "http://searxng:8080"` and `MaxResults: 5`
- [x] 2.2 Create `Features/Search/SearXNGOptions.cs` — record with `Url` and `MaxResults` properties bound to `"SearXNG"` section
- [x] 2.3 Register `SearXNGOptions` in `ServiceCollectionExtensions.cs` and add a named `HttpClient` for SearXNG pointing at `SearXNGOptions.Url`

## 3. SearXNG Client

- [x] 3.1 Create `Features/Search/SearXNGClient.cs` — typed client with `SearchAsync(string query, int maxResults, CancellationToken ct)` returning `IReadOnlyList<SearXNGResult>`
- [x] 3.2 Create `Features/Search/SearXNGResult.cs` — record with `Title`, `Url`, and `Snippet` mapped from SearXNG JSON response (`results[].title`, `.url`, `.content`)
- [x] 3.3 Handle non-2xx and network errors by returning empty list and logging a warning (no throw)

## 4. MCP Tool Registration

- [x] 4.1 Create `Features/Search/SearchWebTool.cs` — `[McpServerTool]` method `search_web(string query)` that calls `SearXNGClient.SearchAsync` and returns results serialized as a JSON string
- [x] 4.2 Register `SearchWebTool` with `mcpBuilder.WithTools<SearchWebTool>()` in `Program.cs`

## 5. Tests

- [x] 5.1 Add unit test for `SearXNGClient` with a mocked `HttpMessageHandler` — happy path returns parsed results, non-2xx returns empty list
- [x] 5.2 Run `dotnet build` and `dotnet test` — all tests pass
