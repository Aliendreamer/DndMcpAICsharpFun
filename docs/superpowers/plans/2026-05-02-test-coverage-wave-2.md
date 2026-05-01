# Test Coverage Wave 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add tests for four 0%-covered production classes and add `[ExcludeFromCodeCoverage]` to Qdrant/DI-wiring infrastructure that is intentionally untested.

**Architecture:** Fix `OllamaEmbeddingService` and `OllamaHealthCheck` to depend on `IOllamaApiClient` (interface), write unit tests with NSubstitute mocks and in-memory PDFs, write HTTP integration tests for `RetrievalEndpoints` using `WebApplication + UseTestServer`, and annotate un-testable infrastructure with `[ExcludeFromCodeCoverage]`.

**Tech Stack:** .NET 10, xUnit 2.9, NSubstitute 5.3, PdfPig (PdfDocumentBuilder), Microsoft.AspNetCore.TestHost, OllamaSharp (IOllamaApiClient), Microsoft.Extensions.Diagnostics.HealthChecks

---

## File Map

**Modified (production):**
- `Features/Embedding/OllamaEmbeddingService.cs` — constructor: `OllamaApiClient` → `IOllamaApiClient`
- `Infrastructure/Ollama/OllamaHealthCheck.cs` — constructor: `OllamaApiClient` → `IOllamaApiClient`
- `Features/VectorStore/QdrantVectorStoreService.cs` — add `[ExcludeFromCodeCoverage]`
- `Infrastructure/Qdrant/QdrantSearchClientAdapter.cs` — add `[ExcludeFromCodeCoverage]`
- `Infrastructure/Qdrant/QdrantCollectionInitializer.cs` — add `[ExcludeFromCodeCoverage]`
- `Infrastructure/Qdrant/QdrantHealthCheck.cs` — add `[ExcludeFromCodeCoverage]`
- `Extensions/ServiceCollectionExtensions.cs` — add `[ExcludeFromCodeCoverage]`
- `Extensions/WebApplicationExtensions.cs` — add `[ExcludeFromCodeCoverage]`
- `Infrastructure/OpenTelemetryOptions.cs` — add `[ExcludeFromCodeCoverage]`
- `Features/Admin/BooksAdminEndpoints.cs` (line with `RegisterBookRequest` record) — add `[ExcludeFromCodeCoverage]`

**Modified (test project):**
- `DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj` — add `ExcludeByFile` coverlet property

**Created (tests):**
- `DndMcpAICsharpFun.Tests/Ingestion/Pdf/PdfPigTextExtractorTests.cs`
- `DndMcpAICsharpFun.Tests/Embedding/OllamaEmbeddingServiceTests.cs`
- `DndMcpAICsharpFun.Tests/Ollama/OllamaHealthCheckTests.cs`
- `DndMcpAICsharpFun.Tests/Retrieval/RetrievalEndpointsTests.cs`

---

## Task 1: Fix IOllamaApiClient abstraction

**Files:**
- Modify: `Features/Embedding/OllamaEmbeddingService.cs`
- Modify: `Infrastructure/Ollama/OllamaHealthCheck.cs`

- [ ] **Step 1.1: Change OllamaEmbeddingService constructor to IOllamaApiClient**

In `Features/Embedding/OllamaEmbeddingService.cs`, change the primary constructor parameter from `OllamaApiClient client` to `IOllamaApiClient client`:

```csharp
public sealed partial class OllamaEmbeddingService(
    IOllamaApiClient client,
    IOptions<OllamaOptions> options,
    ILogger<OllamaEmbeddingService> logger) : IEmbeddingService
```

No other changes needed in this file. The `client.EmbedAsync(...)` call is already defined on `IOllamaApiClient`.

- [ ] **Step 1.2: Change OllamaHealthCheck constructor to IOllamaApiClient**

In `Infrastructure/Ollama/OllamaHealthCheck.cs`, change the primary constructor parameter from `OllamaApiClient client` to `IOllamaApiClient client`:

```csharp
public sealed class OllamaHealthCheck(IOllamaApiClient client) : IHealthCheck
```

No other changes needed. `client.ListLocalModelsAsync(...)` is already on `IOllamaApiClient`.

- [ ] **Step 1.3: Build to verify DI registration still compiles**

```bash
dotnet build DndMcpAICsharpFun.csproj
```

Expected: Build succeeded, 0 errors. The DI registration in `ServiceCollectionExtensions` uses `OllamaApiClient` which implements `IOllamaApiClient`, so it resolves fine without changes.

- [ ] **Step 1.4: Commit the interface fix**

```bash
git add Features/Embedding/OllamaEmbeddingService.cs Infrastructure/Ollama/OllamaHealthCheck.cs
git commit -m "refactor: use IOllamaApiClient in EmbeddingService and HealthCheck"
```

---

## Task 2: Coverage exclusions

**Files:**
- Modify: `Features/VectorStore/QdrantVectorStoreService.cs`
- Modify: `Infrastructure/Qdrant/QdrantSearchClientAdapter.cs`
- Modify: `Infrastructure/Qdrant/QdrantCollectionInitializer.cs`
- Modify: `Infrastructure/Qdrant/QdrantHealthCheck.cs`
- Modify: `Extensions/ServiceCollectionExtensions.cs`
- Modify: `Extensions/WebApplicationExtensions.cs`
- Modify: whichever file contains `OpenTelemetryOptions`
- Modify: `Features/Admin/BooksAdminEndpoints.cs` (the `RegisterBookRequest` record)
- Modify: `DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj`

- [ ] **Step 2.1: Add [ExcludeFromCodeCoverage] to Qdrant classes**

For each of the four Qdrant/VectorStore files, add `using System.Diagnostics.CodeAnalysis;` at the top if not present, then add `[ExcludeFromCodeCoverage]` on the class.

`Features/VectorStore/QdrantVectorStoreService.cs` — add above the class declaration:
```csharp
[ExcludeFromCodeCoverage]
public sealed class QdrantVectorStoreService(...)
```

`Infrastructure/Qdrant/QdrantSearchClientAdapter.cs` — same pattern:
```csharp
[ExcludeFromCodeCoverage]
public sealed class QdrantSearchClientAdapter(...)
```

`Infrastructure/Qdrant/QdrantCollectionInitializer.cs`:
```csharp
[ExcludeFromCodeCoverage]
public sealed class QdrantCollectionInitializer(...)
```

`Infrastructure/Qdrant/QdrantHealthCheck.cs`:
```csharp
[ExcludeFromCodeCoverage]
public sealed class QdrantHealthCheck(...)
```

- [ ] **Step 2.2: Add [ExcludeFromCodeCoverage] to DI-wiring and option classes**

`Extensions/ServiceCollectionExtensions.cs`:
```csharp
[ExcludeFromCodeCoverage]
internal static class ServiceCollectionExtensions
```

`Extensions/WebApplicationExtensions.cs`:
```csharp
[ExcludeFromCodeCoverage]
internal static class WebApplicationExtensions
```

`Infrastructure/OpenTelemetryOptions.cs`:
```csharp
[ExcludeFromCodeCoverage]
public sealed class OpenTelemetryOptions
```

`Features/Admin/BooksAdminEndpoints.cs` — add to the `RegisterBookRequest` record at the bottom of the file:
```csharp
[ExcludeFromCodeCoverage]
public sealed record RegisterBookRequest(
    string SourceName,
    string Version,
    string DisplayName);
```

- [ ] **Step 2.3: Add ExcludeByFile to test project for Program.cs**

In `DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj`, add a `<PropertyGroup>` with the coverlet exclusion. Find the first `<PropertyGroup>` block and add:

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <IsPackable>false</IsPackable>
  <CoverletCollect>true</CoverletCollect>
  <CoverletOutputFormat>cobertura</CoverletOutputFormat>
  <ExcludeByFile>**/Program.cs</ExcludeByFile>
</PropertyGroup>
```

- [ ] **Step 2.4: Build and verify no compilation errors**

```bash
dotnet build
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 2.5: Commit coverage exclusions**

```bash
git add Features/VectorStore/QdrantVectorStoreService.cs \
        Infrastructure/Qdrant/QdrantSearchClientAdapter.cs \
        Infrastructure/Qdrant/QdrantCollectionInitializer.cs \
        Infrastructure/Qdrant/QdrantHealthCheck.cs \
        Extensions/ServiceCollectionExtensions.cs \
        Extensions/WebApplicationExtensions.cs \
        Infrastructure/OpenTelemetryOptions.cs \
        Features/Admin/BooksAdminEndpoints.cs \
        DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj
git commit -m "chore: exclude Qdrant and DI-wiring classes from coverage"
```

(Adjust git add for the OpenTelemetryOptions file based on the actual path found in step 2.2.)

---

## Task 3: PdfPigTextExtractor Tests

**Files:**
- Create: `DndMcpAICsharpFun.Tests/Ingestion/Pdf/PdfPigTextExtractorTests.cs`

The approach mirrors `PdfPigBookmarkReaderTests`: build in-memory PDFs with `PdfDocumentBuilder`, write to a temp file, run the extractor, delete the temp file.

For log verification, a private `CapturingLogger<T>` inner class is used (since `NullLogger.Instance` discards all log entries and `[LoggerMessage]` generated code skips the call when `IsEnabled` returns false).

- [ ] **Step 3.1: Create the test file**

Create `DndMcpAICsharpFun.Tests/Ingestion/Pdf/PdfPigTextExtractorTests.cs`:

```csharp
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Writer;
using UglyToad.PdfPig.Fonts.Standard14Fonts;

namespace DndMcpAICsharpFun.Tests.Ingestion.Pdf;

public sealed class PdfPigTextExtractorTests
{
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public readonly List<(LogLevel Level, string Message)> Entries = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private static PdfPigTextExtractor BuildSut(CapturingLogger<PdfPigTextExtractor>? logger = null, int minChars = 0)
        => new(
            Options.Create(new IngestionOptions { MinPageCharacters = minChars }),
            logger ?? new CapturingLogger<PdfPigTextExtractor>());

    private static string BuildTempPdf(Action<PdfDocumentBuilder> configure)
    {
        var builder = new PdfDocumentBuilder();
        configure(builder);
        var bytes = builder.Build();
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void ExtractPages_SinglePage_ReturnsOnePageWithText()
    {
        var path = BuildTempPdf(b =>
        {
            var page = b.AddPage(PageSize.A4);
            var font = b.AddStandard14Font(Standard14Font.Helvetica);
            page.AddText("Fireball", 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
        });
        try
        {
            var sut = BuildSut();
            var results = sut.ExtractPages(path).ToList();

            Assert.Single(results);
            Assert.Equal(1, results[0].PageNumber);
            Assert.Contains("Fireball", results[0].Text);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExtractPages_MultiPage_ReturnsAllPagesInOrder()
    {
        var path = BuildTempPdf(b =>
        {
            var font = b.AddStandard14Font(Standard14Font.Helvetica);
            for (var i = 1; i <= 3; i++)
            {
                var page = b.AddPage(PageSize.A4);
                page.AddText($"Page{i}", 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
            }
        });
        try
        {
            var sut = BuildSut();
            var results = sut.ExtractPages(path).ToList();

            Assert.Equal(3, results.Count);
            Assert.Equal([1, 2, 3], results.Select(r => r.PageNumber));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExtractPages_PageBelowMinChars_EmitsDebugLog()
    {
        var path = BuildTempPdf(b =>
        {
            // Page with no text — extracted text will be empty (length = 0)
            b.AddPage(PageSize.A4);
        });
        try
        {
            var logger = new CapturingLogger<PdfPigTextExtractor>();
            var sut = BuildSut(logger, minChars: 100); // 0 < 100, triggers log
            _ = sut.ExtractPages(path).ToList(); // materialize

            Assert.Contains(logger.Entries, e =>
                e.Level == LogLevel.Debug && e.Message.Contains("Sparse page"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ExtractPages_PageMeetsMinChars_NoSparseLog()
    {
        var path = BuildTempPdf(b =>
        {
            var font = b.AddStandard14Font(Standard14Font.Helvetica);
            var page = b.AddPage(PageSize.A4);
            // Add enough text to exceed minChars=5
            page.AddText("Hello World This Is Enough Text", 12,
                new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
        });
        try
        {
            var logger = new CapturingLogger<PdfPigTextExtractor>();
            var sut = BuildSut(logger, minChars: 5);
            _ = sut.ExtractPages(path).ToList();

            Assert.DoesNotContain(logger.Entries, e =>
                e.Level == LogLevel.Debug && e.Message.Contains("Sparse page"));
        }
        finally { File.Delete(path); }
    }
}
```

- [ ] **Step 3.2: Run the tests**

```bash
dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj \
  --filter "FullyQualifiedName~PdfPigTextExtractorTests" -v normal
```

Expected: 4 tests pass.

> **Note on `IngestionOptions`:** Verify the property name for the minimum page character threshold is `MinPageCharacters`. If it's named differently, adjust accordingly. Run `grep -r "MinPageCharacters" .` to confirm.

- [ ] **Step 3.3: Commit**

```bash
git add DndMcpAICsharpFun.Tests/Ingestion/Pdf/PdfPigTextExtractorTests.cs
git commit -m "test: add PdfPigTextExtractor unit tests"
```

---

## Task 4: OllamaEmbeddingService Tests

**Files:**
- Create: `DndMcpAICsharpFun.Tests/Embedding/OllamaEmbeddingServiceTests.cs`

- [ ] **Step 4.1: Create the test file**

Create `DndMcpAICsharpFun.Tests/Embedding/OllamaEmbeddingServiceTests.cs`:

```csharp
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OllamaSharp;
using OllamaSharp.Models;

namespace DndMcpAICsharpFun.Tests.Embedding;

public sealed class OllamaEmbeddingServiceTests
{
    private static OllamaEmbeddingService BuildSut(IOllamaApiClient client, string model = "nomic-embed-text")
        => new(client,
            Options.Create(new OllamaOptions { EmbeddingModel = model }),
            NullLogger<OllamaEmbeddingService>.Instance);

    [Fact]
    public async Task EmbedAsync_Success_ReturnsEmbeddingsFromResponse()
    {
        var client = Substitute.For<IOllamaApiClient>();
        var expectedEmbeddings = new float[][] { [0.1f, 0.2f, 0.3f] };
        client.EmbedAsync(Arg.Any<EmbedRequest>(), Arg.Any<CancellationToken>())
            .Returns(new EmbedResponse { Embeddings = expectedEmbeddings });
        var sut = BuildSut(client);

        var result = await sut.EmbedAsync(["some text"]);

        Assert.Single(result);
        Assert.Equal(expectedEmbeddings[0], result[0]);
        await client.Received(1).EmbedAsync(
            Arg.Is<EmbedRequest>(r => r.Model == "nomic-embed-text"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmbedAsync_HttpRequestException_WrapsAsInvalidOperationException()
    {
        var client = Substitute.For<IOllamaApiClient>();
        client.EmbedAsync(Arg.Any<EmbedRequest>(), Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("connection refused"));
        var sut = BuildSut(client, model: "my-model");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.EmbedAsync(["text"]));

        Assert.Contains("my-model", ex.Message);
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }
}
```

- [ ] **Step 4.2: Run the tests**

```bash
dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj \
  --filter "FullyQualifiedName~OllamaEmbeddingServiceTests" -v normal
```

Expected: 2 tests pass.

> **Note on NSubstitute.ExceptionExtensions:** `.Throws(...)` for async methods is in `NSubstitute.ExceptionExtensions`. If you get a compilation error, use `.Returns(_ => throw new HttpRequestException("connection refused"))` instead.

- [ ] **Step 4.3: Commit**

```bash
git add DndMcpAICsharpFun.Tests/Embedding/OllamaEmbeddingServiceTests.cs
git commit -m "test: add OllamaEmbeddingService unit tests"
```

---

## Task 5: OllamaHealthCheck Tests

**Files:**
- Create: `DndMcpAICsharpFun.Tests/Ollama/OllamaHealthCheckTests.cs`

- [ ] **Step 5.1: Create the test file**

Create `DndMcpAICsharpFun.Tests/Ollama/OllamaHealthCheckTests.cs`:

```csharp
using DndMcpAICsharpFun.Infrastructure.Ollama;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OllamaSharp;

namespace DndMcpAICsharpFun.Tests.Ollama;

public sealed class OllamaHealthCheckTests
{
    private static HealthCheckContext MakeContext() => new()
    {
        Registration = new HealthCheckRegistration(
            name: "ollama",
            instance: Substitute.For<IHealthCheck>(),
            failureStatus: null,
            tags: null)
    };

    [Fact]
    public async Task CheckHealthAsync_WhenOllamaResponds_ReturnsHealthy()
    {
        var client = Substitute.For<IOllamaApiClient>();
        var sut = new OllamaHealthCheck(client);

        var result = await sut.CheckHealthAsync(MakeContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task CheckHealthAsync_WhenOllamaUnreachable_ReturnsUnhealthy()
    {
        var client = Substitute.For<IOllamaApiClient>();
        var exception = new HttpRequestException("Connection refused");
        client.ListLocalModelsAsync(Arg.Any<CancellationToken>())
            .Throws(exception);
        var sut = new OllamaHealthCheck(client);

        var result = await sut.CheckHealthAsync(MakeContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Ollama is unreachable", result.Description);
        Assert.Same(exception, result.Exception);
    }
}
```

> **Note on `ListLocalModelsAsync` return type:** If `IOllamaApiClient.ListLocalModelsAsync` returns `Task<T>` for some `T`, NSubstitute's `.Throws(exception)` will throw synchronously when the method is called, which the `try/catch` in `CheckHealthAsync` will catch. If there is a compile error, change to: `client.When(x => x.ListLocalModelsAsync(Arg.Any<CancellationToken>())).Do(_ => throw exception);`

- [ ] **Step 5.2: Run the tests**

```bash
dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj \
  --filter "FullyQualifiedName~OllamaHealthCheckTests" -v normal
```

Expected: 2 tests pass.

- [ ] **Step 5.3: Commit**

```bash
git add DndMcpAICsharpFun.Tests/Ollama/OllamaHealthCheckTests.cs
git commit -m "test: add OllamaHealthCheck unit tests"
```

---

## Task 6: RetrievalEndpoints Tests

**Files:**
- Create: `DndMcpAICsharpFun.Tests/Retrieval/RetrievalEndpointsTests.cs`

`SearchPublic` and `SearchDiagnostic` receive `IRagRetrievalService` via minimal API parameter injection (DI). The admin middleware applies to all `/admin/*` paths and checks for `X-Admin-Api-Key` header.

- [ ] **Step 6.1: Create the test file**

Create `DndMcpAICsharpFun.Tests/Retrieval/RetrievalEndpointsTests.cs`:

```csharp
using System.Net;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Retrieval;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace DndMcpAICsharpFun.Tests.Retrieval;

public sealed class RetrievalEndpointsTests
{
    private const string AdminKey = "test-key";

    private static async Task<(HttpClient Client, IRagRetrievalService Retrieval)> BuildClientAsync()
    {
        var retrieval = Substitute.For<IRagRetrievalService>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(retrieval);
        builder.Services.Configure<AdminOptions>(o => o.ApiKey = AdminKey);

        var app = builder.Build();
        // Inline the admin middleware (MapAdminMiddleware() is internal — can't call from test project)
        app.UseWhen(
            ctx => ctx.Request.Path.StartsWithSegments("/admin"),
            adminApp => adminApp.UseMiddleware<AdminApiKeyMiddleware>());
        app.MapRetrievalEndpoints();

        await app.StartAsync();
        return (app.GetTestClient(), retrieval);
    }

    // ── Public search ────────────────────────────────────────────────

    [Fact]
    public async Task SearchPublic_MissingQ_Returns400()
    {
        var (client, _) = await BuildClientAsync();

        var response = await client.GetAsync("/retrieval/search");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SearchPublic_WhitespaceQ_Returns400()
    {
        var (client, _) = await BuildClientAsync();

        var response = await client.GetAsync("/retrieval/search?q=   ");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SearchPublic_ValidQ_Returns200AndCallsSearchAsync()
    {
        var (client, retrieval) = await BuildClientAsync();
        retrieval.SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<RetrievalResult>>([]));

        var response = await client.GetAsync("/retrieval/search?q=fireball");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await retrieval.Received(1).SearchAsync(
            Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchPublic_ValidVersionAndCategory_ParsedCorrectly()
    {
        var (client, retrieval) = await BuildClientAsync();
        retrieval.SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<RetrievalResult>>([]));

        await client.GetAsync("/retrieval/search?q=fireball&version=Edition2024&category=Spell");

        await retrieval.Received(1).SearchAsync(
            Arg.Is<RetrievalQuery>(q =>
                q.Version == DndVersion.Edition2024 &&
                q.Category == ContentCategory.Spell),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SearchPublic_InvalidVersionAndCategory_NullFilters()
    {
        var (client, retrieval) = await BuildClientAsync();
        retrieval.SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<RetrievalResult>>([]));

        await client.GetAsync("/retrieval/search?q=fireball&version=invalid&category=invalid");

        await retrieval.Received(1).SearchAsync(
            Arg.Is<RetrievalQuery>(q =>
                q.Version == null &&
                q.Category == null),
            Arg.Any<CancellationToken>());
    }

    // ── Admin diagnostic search ──────────────────────────────────────

    [Fact]
    public async Task SearchDiagnostic_MissingAdminKey_Returns401()
    {
        var (client, _) = await BuildClientAsync();

        var response = await client.GetAsync("/admin/retrieval/search?q=fireball");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SearchDiagnostic_ValidAdminKey_Returns200AndCallsSearchDiagnosticAsync()
    {
        var (client, retrieval) = await BuildClientAsync();
        retrieval.SearchDiagnosticAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<RetrievalDiagnosticResult>>([]));

        var request = new HttpRequestMessage(HttpMethod.Get, "/admin/retrieval/search?q=fireball");
        request.Headers.Add("X-Admin-Api-Key", AdminKey);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await retrieval.Received(1).SearchDiagnosticAsync(
            Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>());
    }
}
```

> **Note on admin middleware:** `MapAdminMiddleware()` is `internal`, so the test inlines the same `UseWhen` logic directly. `AdminApiKeyMiddleware` is `public` and accessible from the test project.

> **Note on `MapRetrievalEndpoints`:** `RetrievalEndpoints` is `public static`, so it's accessible from the test project.

- [ ] **Step 6.2: Run the tests**

```bash
dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj \
  --filter "FullyQualifiedName~RetrievalEndpointsTests" -v normal
```

Expected: 7 tests pass.

- [ ] **Step 6.3: Commit**

```bash
git add DndMcpAICsharpFun.Tests/Retrieval/RetrievalEndpointsTests.cs
git commit -m "test: add RetrievalEndpoints HTTP integration tests"
```

---

## Task 7: Final Verification

- [ ] **Step 7.1: Run full test suite**

```bash
dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj -v minimal
```

Expected: All tests pass (103 baseline + ~15 new = ~118 total), 0 failures.

- [ ] **Step 7.2: Generate coverage report**

```bash
dotnet test DndMcpAICsharpFun.Tests/DndMcpAICsharpFun.Tests.csproj \
  /p:CollectCoverage=true \
  /p:CoverletOutputFormat=cobertura \
  /p:CoverletOutput=./TestResults/coverage.cobertura.xml \
  /p:ExcludeByFile="**/Program.cs"

reportgenerator \
  -reports:DndMcpAICsharpFun.Tests/TestResults/coverage.cobertura.xml \
  -targetdir:TestResults/CoverageReport \
  -reporttypes:TextSummary
```

Expected: Overall line coverage ≥ 73%. `PdfPigTextExtractor`, `OllamaEmbeddingService`, `OllamaHealthCheck`, `RetrievalEndpoints` appear with meaningful coverage. Qdrant classes absent from report.

- [ ] **Step 7.3: Final commit**

If no uncommitted changes, the work is done. Otherwise:

```bash
git add -A
git commit -m "chore: verify test coverage wave 2 complete"
```
