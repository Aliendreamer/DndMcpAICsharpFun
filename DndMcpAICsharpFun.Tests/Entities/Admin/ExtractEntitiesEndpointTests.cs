using System.Net;
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DndMcpAICsharpFun.Tests.Entities.Admin;

public sealed class ExtractEntitiesEndpointTests
{
    private static async Task<(
        HttpClient Client,
        IIngestionTracker Tracker,
        IIngestionQueue Queue)> BuildClientAsync(string canonicalDir)
    {
        var tracker = Substitute.For<IIngestionTracker>();
        var queue = Substitute.For<IIngestionQueue>();
        var deletionService = Substitute.For<IBookDeletionService>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(tracker);
        builder.Services.AddSingleton(queue);
        builder.Services.AddSingleton(deletionService);
        builder.Services.AddSingleton<ILogger<RegisterBookRequest>>(
            NullLogger<RegisterBookRequest>.Instance);
        builder.Services.Configure<AdminOptions>(o => o.ApiKey = "test-key");
        builder.Services.Configure<IngestionOptions>(o => o.BooksPath = Path.GetTempPath());
        builder.Services.Configure<EntityExtractionOptions>(o => o.CanonicalDirectory = canonicalDir);

        var app = builder.Build();
        app.MapGroup("/admin").MapBooksAdmin();

        await app.StartAsync();
        return (app.GetTestClient(), tracker, queue);
    }

    private static IngestionRecord MakeRecord(
        int id = 1,
        IngestionStatus status = IngestionStatus.JsonIngested,
        string displayName = "Test Book") => new()
    {
        Id = id,
        FilePath = "/tmp/test.pdf",
        FileName = "test.pdf",
        FileHash = "h",
        Version = "5e",
        DisplayName = displayName,
        Status = status,
    };

    [Fact]
    public async Task ExtractEntities_RecordNotFound_Returns404()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var (client, tracker, _) = await BuildClientAsync(tempDir);
        tracker.GetByIdAsync(9999, Arg.Any<CancellationToken>()).Returns((IngestionRecord?)null);

        var response = await client.PostAsync("/admin/books/9999/extract-entities", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ExtractEntities_CanonicalExistsWithoutForce_Returns409()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var canonicalPath = Path.Combine(tempDir, "test-book.json");
            await File.WriteAllTextAsync(canonicalPath, "{}");

            var (client, tracker, queue) = await BuildClientAsync(tempDir);
            tracker.GetByIdAsync(1, Arg.Any<CancellationToken>())
                .Returns(MakeRecord(1));

            var response = await client.PostAsync("/admin/books/1/extract-entities", null);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            queue.DidNotReceiveWithAnyArgs().TryEnqueue(default!);
        }
        finally { Directory.Delete(tempDir, true); }
    }

    [Fact]
    public async Task ExtractEntities_ForceTrueWithExistingCanonical_Returns202_AndEnqueues()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var canonicalPath = Path.Combine(tempDir, "test-book.json");
            await File.WriteAllTextAsync(canonicalPath, "{}");

            var (client, tracker, queue) = await BuildClientAsync(tempDir);
            tracker.GetByIdAsync(1, Arg.Any<CancellationToken>())
                .Returns(MakeRecord(1));
            queue.TryEnqueue(Arg.Any<IngestionWorkItem>()).Returns(true);

            var response = await client.PostAsync("/admin/books/1/extract-entities?force=true", null);

            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            queue.Received(1).TryEnqueue(Arg.Is<IngestionWorkItem>(i =>
                i.Type == IngestionWorkType.ExtractEntities && i.BookId == 1 && i.Force));
        }
        finally { Directory.Delete(tempDir, true); }
    }
}
