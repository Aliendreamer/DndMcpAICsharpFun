## 1. NuGet Packages

- [x] 1.1 Add `Qdrant.Client` to `DndMcpAICsharpFun.csproj`
- [x] 1.2 Add `OllamaSharp` to `DndMcpAICsharpFun.csproj`
- [x] 1.3 Add `UglyToad.PdfPig` to `DndMcpAICsharpFun.csproj`
- [x] 1.4 Add `Microsoft.Data.Sqlite` to `DndMcpAICsharpFun.csproj`
- [x] 1.5 Run `dotnet restore` and confirm no package errors

## 2. Folder Structure

- [x] 2.1 Create `Domain/` directory with a `.gitkeep`
- [x] 2.2 Create `Features/Ingestion/`, `Features/Embedding/`, `Features/VectorStore/`, `Features/Retrieval/`, `Features/Admin/` directories
- [x] 2.3 Create `Infrastructure/Ollama/`, `Infrastructure/Qdrant/`, `Infrastructure/Sqlite/` directories

## 3. Typed Options

- [x] 3.1 Create `Infrastructure/Qdrant/QdrantOptions.cs` with `Host`, `Port`, `ApiKey` properties
- [x] 3.2 Create `Infrastructure/Ollama/OllamaOptions.cs` with `BaseUrl`, `EmbeddingModel` properties
- [x] 3.3 Create `Infrastructure/Sqlite/IngestionOptions.cs` with `BooksPath`, `DatabasePath` properties
- [x] 3.4 Create `Features/Admin/AdminOptions.cs` with `ApiKey` property
- [x] 3.5 Update `Config/appsettings.json` with `Qdrant`, `Ollama`, `Ingestion`, and `Admin` sections
- [x] 3.6 Update `Config/appsettings.Development.json` with local dev values (localhost URLs, placeholder key)

## 4. Infrastructure Client Registrations

- [x] 4.1 Register `IOptions<QdrantOptions>`, `IOptions<OllamaOptions>`, `IOptions<IngestionOptions>`, `IOptions<AdminOptions>` in `Program.cs`
- [x] 4.2 Register `QdrantClient` as singleton, constructed from `QdrantOptions`
- [x] 4.3 Register `OllamaApiClient` as singleton, constructed from `OllamaOptions.BaseUrl`
- [x] 4.4 Add startup guard: throw if `AdminOptions.ApiKey` is null or empty

## 5. Health Checks

- [x] 5.1 Add `Microsoft.Extensions.Diagnostics.HealthChecks` (included in SDK — verify no extra package needed)
- [x] 5.2 Create `Infrastructure/Qdrant/QdrantHealthCheck.cs` implementing `IHealthCheck` (ping Qdrant collections endpoint)
- [x] 5.3 Create `Infrastructure/Ollama/OllamaHealthCheck.cs` implementing `IHealthCheck` (ping Ollama `/api/tags`)
- [x] 5.4 Register both health checks in `Program.cs`
- [x] 5.5 Map `GET /health` (liveness) and `GET /health/ready` (readiness with both checks) in `Program.cs`

## 6. Admin API Key Middleware

- [x] 6.1 Create `Features/Admin/AdminApiKeyMiddleware.cs` that reads `X-Admin-Api-Key` header and returns 401 if invalid
- [x] 6.2 Apply middleware conditionally to `/admin/*` routes only in `Program.cs`

## 7. Docker

- [x] 7.1 Create `Dockerfile` with multi-stage build (sdk:10.0 → aspnet:10.0)
- [x] 7.2 Create `docker-compose.yml` with `app`, `qdrant`, `ollama` services on a shared network
- [x] 7.3 Add named volume for books (`books_data` mounted at `Ingestion:BooksPath`)
- [x] 7.4 Add named volume for Qdrant storage (`qdrant_data`)
- [x] 7.5 Configure `app` service `depends_on` with health check conditions for `qdrant` and `ollama`
- [x] 7.6 Pass `Admin__ApiKey` as environment variable in `docker-compose.yml` (value from `.env` file)
- [x] 7.7 Add `.env.example` with all required environment variables documented
- [x] 7.8 Add `.env` to `.gitignore`

## 8. Verification

- [x] 8.1 `dotnet build` passes with zero errors and zero warnings
- [x] 8.2 `docker compose build` produces the app image successfully
- [x] 8.3 `docker compose up` brings all three services to healthy state
- [x] 8.4 `GET /health/ready` returns 200 when Qdrant and Ollama are running
- [x] 8.5 `GET /admin/test` (non-existent route) returns 401 without key and 404 with valid key
