using System.Net;
using System.Net.Http.Json;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DndMcpAICsharpFun.Tests.Admin;

public sealed class BooksAdminEndpointsTests
{
    private static async Task<(
        HttpClient Client,
        IIngestionTracker Tracker,
        IIngestionQueue Queue,
        IIngestionOrchestrator Orchestrator,
        IEntityJsonStore JsonStore,
        IExtractionCancellationRegistry Registry)> BuildClientAsync()
    {
        var tracker = Substitute.For<IIngestionTracker>();
        var queue = Substitute.For<IIngestionQueue>();
        var orchestrator = Substitute.For<IIngestionOrchestrator>();
        var jsonStore = Substitute.For<IEntityJsonStore>();
        var registry = Substitute.For<IExtractionCancellationRegistry>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(tracker);
        builder.Services.AddSingleton(queue);
        builder.Services.AddSingleton(orchestrator);
        builder.Services.AddSingleton(jsonStore);
        builder.Services.AddSingleton(registry);
        builder.Services.AddSingleton<ILogger<RegisterBookRequest>>(
            NullLogger<RegisterBookRequest>.Instance);
        builder.Services.AddSingleton<ILogger<RegisterBookByPathRequest>>(
            NullLogger<RegisterBookByPathRequest>.Instance);
        builder.Services.Configure<AdminOptions>(o => o.ApiKey = "test-key");
        builder.Services.Configure<IngestionOptions>(o => o.BooksPath = Path.GetTempPath());

        var app = builder.Build();
        // No middleware — bypass auth, test endpoint logic only
        app.MapGroup("/admin").MapBooksAdmin();

        await app.StartAsync();
        return (app.GetTestClient(), tracker, queue, orchestrator, jsonStore, registry);
    }

    private static IngestionRecord MakeRecord(
        int id = 1,
        IngestionStatus status = IngestionStatus.Pending) => new()
    {
        Id = id,
        FilePath = "/tmp/test.pdf",
        FileName = "test.pdf",
        FileHash = string.Empty,
        SourceName = "PHB",
        Version = "5e",
        DisplayName = "Player's Handbook",
        Status = status,
    };

    // POST /admin/books/register
    [Fact]
    public async Task RegisterBook_ValidPdf_Returns202()
    {
        var (client, tracker, _, _, _, _) = await BuildClientAsync();
        tracker.CreateAsync(Arg.Any<IngestionRecord>(), Arg.Any<CancellationToken>())
            .Returns(MakeRecord());

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent([0x25, 0x50, 0x44, 0x46]), "file", "test.pdf");
        content.Add(new StringContent("PHB"), "sourceName");
        content.Add(new StringContent("Edition2014"), "version");
        content.Add(new StringContent("Player's Handbook"), "displayName");

        var response = await client.PostAsync("/admin/books/register", content);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await tracker.Received(1).CreateAsync(Arg.Any<IngestionRecord>(), Arg.Any<CancellationToken>());
        // Clean up file created by the endpoint
        foreach (var f in Directory.GetFiles(Path.GetTempPath(), "*_test.pdf"))
            File.Delete(f);
    }

    [Fact]
    public async Task RegisterBook_NonPdfExtension_Returns400()
    {
        var (client, _, _, _, _, _) = await BuildClientAsync();

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent([0x00]), "file", "test.docx");
        content.Add(new StringContent("PHB"), "sourceName");
        content.Add(new StringContent("5e"), "version");
        content.Add(new StringContent("PHB"), "displayName");

        var response = await client.PostAsync("/admin/books/register", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterBook_InvalidVersion_Returns400()
    {
        var (client, _, _, _, _, _) = await BuildClientAsync();

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent([0x25, 0x50, 0x44, 0x46]), "file", "test.pdf");
        content.Add(new StringContent("PHB"), "sourceName");
        content.Add(new StringContent("invalid_version"), "version");
        content.Add(new StringContent("PHB"), "displayName");

        var response = await client.PostAsync("/admin/books/register", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // POST /admin/books/register-path
    [Fact]
    public async Task RegisterBookByPath_ValidPath_Returns202()
    {
        var (client, tracker, _, _, _, _) = await BuildClientAsync();
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(tempFile, [0x25, 0x50, 0x44, 0x46]);
        try
        {
            tracker.CreateAsync(Arg.Any<IngestionRecord>(), Arg.Any<CancellationToken>())
                .Returns(MakeRecord());

            var response = await client.PostAsJsonAsync("/admin/books/register-path", new
            {
                filePath = tempFile,
                sourceName = "PHB",
                version = "Edition2014",
                displayName = "Player's Handbook"
            });

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }
        finally { File.Delete(tempFile); }
    }

    [Fact]
    public async Task RegisterBookByPath_FileNotFound_Returns400()
    {
        var (client, _, _, _, _, _) = await BuildClientAsync();

        var response = await client.PostAsJsonAsync("/admin/books/register-path", new
        {
            filePath = "/nonexistent/path/file.pdf",
            sourceName = "PHB",
            version = "5e",
            displayName = "PHB"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterBookByPath_InvalidVersion_Returns400()
    {
        var (client, _, _, _, _, _) = await BuildClientAsync();
        var tempFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(tempFile, [0x25, 0x50, 0x44, 0x46]);
        try
        {
            var response = await client.PostAsJsonAsync("/admin/books/register-path", new
            {
                filePath = tempFile,
                sourceName = "PHB",
                version = "bad_version",
                displayName = "PHB"
            });

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally { File.Delete(tempFile); }
    }

    // GET /admin/books
    [Fact]
    public async Task GetAllBooks_Returns200WithList()
    {
        var (client, tracker, _, _, _, _) = await BuildClientAsync();
        tracker.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<IngestionRecord>>([MakeRecord(1), MakeRecord(2)]));

        var response = await client.GetAsync("/admin/books");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // POST /admin/books/{id}/extract
    [Fact]
    public async Task ExtractBook_NotFound_Returns404()
    {
        var (client, tracker, _, _, _, _) = await BuildClientAsync();
        tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns((IngestionRecord?)null);

        var response = await client.PostAsync("/admin/books/1/extract", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ExtractBook_AlreadyProcessing_Returns409()
    {
        var (client, tracker, _, _, _, _) = await BuildClientAsync();
        tracker.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(MakeRecord(1, IngestionStatus.Processing));

        var response = await client.PostAsync("/admin/books/1/extract", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ExtractBook_Success_Returns202AndEnqueues()
    {
        var (client, tracker, queue, _, _, _) = await BuildClientAsync();
        tracker.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(MakeRecord(1, IngestionStatus.Pending));

        var response = await client.PostAsync("/admin/books/1/extract", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        queue.Received(1).TryEnqueue(Arg.Is<IngestionWorkItem>(w =>
            w.Type == IngestionWorkType.Extract && w.BookId == 1));
    }

    // GET /admin/books/{id}/extracted
    [Fact]
    public async Task GetExtracted_NotFound_Returns404()
    {
        var (client, tracker, _, _, _, _) = await BuildClientAsync();
        tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns((IngestionRecord?)null);

        var response = await client.GetAsync("/admin/books/1/extracted");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetExtracted_Success_Returns200()
    {
        var (client, tracker, _, _, jsonStore, _) = await BuildClientAsync();
        tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(MakeRecord(1));
        jsonStore.ListPageFiles(1).Returns(["page_1.json", "page_2.json"]);

        var response = await client.GetAsync("/admin/books/1/extracted");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // POST /admin/books/{id}/ingest-json
    [Fact]
    public async Task IngestJson_NotFound_Returns404()
    {
        var (client, tracker, _, _, _, _) = await BuildClientAsync();
        tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns((IngestionRecord?)null);

        var response = await client.PostAsync("/admin/books/1/ingest-json", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task IngestJson_AlreadyProcessing_Returns409()
    {
        var (client, tracker, _, _, _, _) = await BuildClientAsync();
        tracker.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(MakeRecord(1, IngestionStatus.Processing));

        var response = await client.PostAsync("/admin/books/1/ingest-json", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task IngestJson_Success_Returns202()
    {
        var (client, tracker, queue, _, _, _) = await BuildClientAsync();
        tracker.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(MakeRecord(1, IngestionStatus.Extracted));

        var response = await client.PostAsync("/admin/books/1/ingest-json", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        queue.Received(1).TryEnqueue(Arg.Is<IngestionWorkItem>(w =>
            w.Type == IngestionWorkType.IngestJson && w.BookId == 1));
    }

    // DELETE /admin/books/{id}
    [Fact]
    public async Task DeleteBook_NotFound_Returns404()
    {
        var (client, _, _, orchestrator, _, _) = await BuildClientAsync();
        orchestrator.DeleteBookAsync(1, Arg.Any<CancellationToken>())
            .Returns(DeleteBookResult.NotFound);

        var response = await client.DeleteAsync("/admin/books/1");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteBook_Conflict_Returns409()
    {
        var (client, _, _, orchestrator, _, _) = await BuildClientAsync();
        orchestrator.DeleteBookAsync(1, Arg.Any<CancellationToken>())
            .Returns(DeleteBookResult.Conflict);

        var response = await client.DeleteAsync("/admin/books/1");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task DeleteBook_Success_Returns204()
    {
        var (client, _, _, orchestrator, _, _) = await BuildClientAsync();
        orchestrator.DeleteBookAsync(1, Arg.Any<CancellationToken>())
            .Returns(DeleteBookResult.Deleted);

        var response = await client.DeleteAsync("/admin/books/1");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // POST /admin/books/{id}/cancel-extract
    [Fact]
    public async Task CancelExtract_NotFound_Returns404()
    {
        var (client, _, _, _, _, registry) = await BuildClientAsync();
        registry.Cancel(1).Returns(false);

        var response = await client.PostAsync("/admin/books/1/cancel-extract", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CancelExtract_Success_Returns200()
    {
        var (client, _, _, _, _, registry) = await BuildClientAsync();
        registry.Cancel(1).Returns(true);

        var response = await client.PostAsync("/admin/books/1/cancel-extract", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
