using System.Net;
using System.Text.Json;

using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Infrastructure.Ingestion;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DndMcpAICsharpFun.Tests.Admin;

public sealed class BooksAdminEndpointsTests : IDisposable
{
    private readonly string _booksDir =
        Path.Combine(Path.GetTempPath(), "books-admin-tests-" + Path.GetRandomFileName());

    private async Task<(
        HttpClient Client,
        IIngestionTracker Tracker,
        IIngestionQueue Queue,
        IBookDeletionService DeletionService)> BuildClientAsync()
    {
        var tracker = Substitute.For<IIngestionTracker>();
        var queue = Substitute.For<IIngestionQueue>();
        var deletionService = Substitute.For<IBookDeletionService>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(tracker);
        builder.Services.AddSingleton(queue);
        builder.Services.AddSingleton(deletionService);
        builder.Services.AddSingleton(
            new BookSourceRegistry(TestPaths.RepoFile("5etools/books.json")));
        builder.Services.AddSingleton<ILogger<BookRegistrationService>>(
            NullLogger<BookRegistrationService>.Instance);
        builder.Services.AddScoped<BookRegistrationService>();
        builder.Services.Configure<AdminOptions>(o => o.ApiKey = "test-key");
        Directory.CreateDirectory(_booksDir);
        builder.Services.Configure<IngestionOptions>(o => o.BooksPath = _booksDir);

        var app = builder.Build();
        app.MapGroup("/admin").MapBooksAdmin();

        await app.StartAsync();
        return (app.GetTestClient(), tracker, queue, deletionService);
    }

    public void Dispose()
    {
        if (Directory.Exists(_booksDir))
            Directory.Delete(_booksDir, recursive: true);
    }

    private static IngestionRecord MakeRecord(
        int id = 1,
        IngestionStatus status = IngestionStatus.Pending) => new()
        {
            Id = id,
            FilePath = "/tmp/test.pdf",
            FileName = "test.pdf",
            FileHash = string.Empty,
            Version = "5e",
            DisplayName = "Player's Handbook",
            Status = status,
        };

    // POST /admin/books/register
    [Fact]
    public async Task RegisterBook_ValidPdf_Returns201()
    {
        var (client, tracker, _, _) = await BuildClientAsync();
        tracker.CreateAsync(Arg.Any<IngestionRecord>(), Arg.Any<CancellationToken>())
            .Returns(MakeRecord());

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent([0x25, 0x50, 0x44, 0x46]), "file", "test.pdf");
        content.Add(new StringContent("Edition2014"), "version");
        content.Add(new StringContent("Player's Handbook"), "displayName");

        var response = await client.PostAsync("/admin/books/register", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await tracker.Received(1).CreateAsync(
            Arg.Is<IngestionRecord>(r => r.DisplayName == "Player's Handbook"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterBook_NonPdfExtension_Returns400()
    {
        var (client, _, _, _) = await BuildClientAsync();

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent([0x00]), "file", "test.docx");
        content.Add(new StringContent("5e"), "version");
        content.Add(new StringContent("PHB"), "displayName");

        var response = await client.PostAsync("/admin/books/register", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterBook_InvalidVersion_Returns400()
    {
        var (client, _, _, _) = await BuildClientAsync();

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent([0x25, 0x50, 0x44, 0x46]), "file", "test.pdf");
        content.Add(new StringContent("invalid_version"), "version");
        content.Add(new StringContent("PHB"), "displayName");

        var response = await client.PostAsync("/admin/books/register", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterBook_WithBookType_StoresParsedValue()
    {
        var (client, tracker, _, _) = await BuildClientAsync();
        tracker.CreateAsync(Arg.Any<IngestionRecord>(), Arg.Any<CancellationToken>())
            .Returns(MakeRecord());

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent([0x25, 0x50, 0x44, 0x46]), "file", "test.pdf");
        content.Add(new StringContent("Edition2014"), "version");
        content.Add(new StringContent("Player's Handbook"), "displayName");
        content.Add(new StringContent("supplement"), "bookType"); // case-insensitive

        var response = await client.PostAsync("/admin/books/register", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await tracker.Received(1).CreateAsync(
            Arg.Is<IngestionRecord>(r => r.BookType == DndMcpAICsharpFun.Domain.BookType.Supplement),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterBook_MissingBookType_DefaultsToUnknown()
    {
        var (client, tracker, _, _) = await BuildClientAsync();
        tracker.CreateAsync(Arg.Any<IngestionRecord>(), Arg.Any<CancellationToken>())
            .Returns(MakeRecord());

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent([0x25, 0x50, 0x44, 0x46]), "file", "test.pdf");
        content.Add(new StringContent("Edition2014"), "version");
        content.Add(new StringContent("Player's Handbook"), "displayName");

        var response = await client.PostAsync("/admin/books/register", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await tracker.Received(1).CreateAsync(
            Arg.Is<IngestionRecord>(r => r.BookType == DndMcpAICsharpFun.Domain.BookType.Unknown),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterBook_InvalidBookType_DefaultsToUnknown()
    {
        var (client, tracker, _, _) = await BuildClientAsync();
        tracker.CreateAsync(Arg.Any<IngestionRecord>(), Arg.Any<CancellationToken>())
            .Returns(MakeRecord());

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent([0x25, 0x50, 0x44, 0x46]), "file", "test.pdf");
        content.Add(new StringContent("Edition2014"), "version");
        content.Add(new StringContent("Player's Handbook"), "displayName");
        content.Add(new StringContent("garbage"), "bookType");

        var response = await client.PostAsync("/admin/books/register", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await tracker.Received(1).CreateAsync(
            Arg.Is<IngestionRecord>(r => r.BookType == DndMcpAICsharpFun.Domain.BookType.Unknown),
            Arg.Any<CancellationToken>());
    }

    // GET /admin/books
    [Fact]
    public async Task GetAllBooks_Returns200WithList()
    {
        var (client, tracker, _, _) = await BuildClientAsync();
        tracker.GetAllAsync().Returns(Task.FromResult(new List<IngestionRecord> { MakeRecord() }));

        var response = await client.GetAsync("/admin/books");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // DELETE /admin/books/{id}
    [Fact]
    public async Task DeleteBook_Deleted_Returns204()
    {
        var (client, _, _, deletionService) = await BuildClientAsync();
        deletionService.DeleteBookAsync(1, Arg.Any<CancellationToken>())
            .Returns(DeleteBookResult.Deleted);

        var response = await client.DeleteAsync("/admin/books/1");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteBook_NotFound_Returns404()
    {
        var (client, _, _, deletionService) = await BuildClientAsync();
        deletionService.DeleteBookAsync(99, Arg.Any<CancellationToken>())
            .Returns(DeleteBookResult.NotFound);

        var response = await client.DeleteAsync("/admin/books/99");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteBook_Conflict_Returns409()
    {
        var (client, _, _, deletionService) = await BuildClientAsync();
        deletionService.DeleteBookAsync(1, Arg.Any<CancellationToken>())
            .Returns(DeleteBookResult.Conflict);

        var response = await client.DeleteAsync("/admin/books/1");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // POST /admin/books/{id}/ingest-blocks
    [Fact]
    public async Task IngestBlocks_RecordNotFound_Returns404()
    {
        var (client, tracker, _, _) = await BuildClientAsync();
        tracker.GetByIdAsync(99, Arg.Any<CancellationToken>()).Returns((IngestionRecord?)null);

        var response = await client.PostAsync("/admin/books/99/ingest-blocks", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task IngestBlocks_AlreadyProcessing_Returns409()
    {
        var (client, tracker, _, _) = await BuildClientAsync();
        tracker.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(MakeRecord(1, IngestionStatus.Processing));

        var response = await client.PostAsync("/admin/books/1/ingest-blocks", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task IngestBlocks_Success_Returns202_AndEnqueues()
    {
        var (client, tracker, queue, _) = await BuildClientAsync();
        tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(MakeRecord(1));

        var response = await client.PostAsync("/admin/books/1/ingest-blocks", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        queue.Received(1).TryEnqueue(Arg.Is<IngestionWorkItem>(w =>
            w.Type == IngestionWorkType.IngestBlocks && w.BookId == 1));
    }

    // POST /admin/books/register — fivetoolsSourceKey tests

    [Fact]
    public async Task RegisterBook_WithValidSourceKey_StoresKey()
    {
        var (client, tracker, _, _) = await BuildClientAsync();
        tracker.CreateAsync(Arg.Any<IngestionRecord>(), Arg.Any<CancellationToken>())
            .Returns(MakeRecord());

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent([0x25, 0x50, 0x44, 0x46]), "file", "test.pdf");
        content.Add(new StringContent("Edition2014"), "version");
        content.Add(new StringContent("Player's Handbook"), "displayName");
        content.Add(new StringContent("PHB"), "fivetoolsSourceKey");

        var response = await client.PostAsync("/admin/books/register", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await tracker.Received(1).CreateAsync(
            Arg.Is<IngestionRecord>(r => r.FivetoolsSourceKey == "PHB"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RegisterBook_WithUnknownSourceKey_Returns422()
    {
        var (client, _, _, _) = await BuildClientAsync();

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent([0x25, 0x50, 0x44, 0x46]), "file", "test.pdf");
        content.Add(new StringContent("Edition2014"), "version");
        content.Add(new StringContent("Player's Handbook"), "displayName");
        content.Add(new StringContent("NOTABOOK"), "fivetoolsSourceKey");

        var response = await client.PostAsync("/admin/books/register", content);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task RegisterBook_NoSourceKey_ResponseIncludesSuggestedSources()
    {
        var (client, tracker, _, _) = await BuildClientAsync();
        tracker.CreateAsync(Arg.Any<IngestionRecord>(), Arg.Any<CancellationToken>())
            .Returns(MakeRecord());

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent([0x25, 0x50, 0x44, 0x46]), "file", "test.pdf");
        content.Add(new StringContent("Edition2014"), "version");
        content.Add(new StringContent("Player's Handbook"), "displayName");
        // No fivetoolsSourceKey

        var response = await client.PostAsync("/admin/books/register", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("suggestedSources", body, StringComparison.OrdinalIgnoreCase);
    }
}