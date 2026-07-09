using System.Net;

using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Infrastructure.Ingestion;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DndMcpAICsharpFun.Tests.Entities.Admin;

public sealed class IngestEntitiesEndpointTests
{
    private static async Task<(
        HttpClient Client,
        IIngestionTracker Tracker,
        IIngestionQueue Queue)> BuildClientAsync()
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

        var app = builder.Build();
        app.MapGroup("/admin").MapBooksAdmin();

        await app.StartAsync();
        return (app.GetTestClient(), tracker, queue);
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

    [Fact]
    public async Task IngestEntities_RecordNotFound_Returns404()
    {
        var (client, tracker, _) = await BuildClientAsync();
        tracker.GetByIdAsync(9999, Arg.Any<CancellationToken>()).Returns((IngestionRecord?)null);

        var response = await client.PostAsync("/admin/books/9999/ingest-entities", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task IngestEntities_AlreadyProcessing_Returns409()
    {
        var (client, tracker, _) = await BuildClientAsync();
        tracker.GetByIdAsync(1, Arg.Any<CancellationToken>())
            .Returns(MakeRecord(1, IngestionStatus.Processing));

        var response = await client.PostAsync("/admin/books/1/ingest-entities", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task IngestEntities_Success_Returns202_AndEnqueues()
    {
        var (client, tracker, queue) = await BuildClientAsync();
        tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(MakeRecord(1));

        var response = await client.PostAsync("/admin/books/1/ingest-entities", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        queue.Received(1).TryEnqueue(Arg.Is<IngestionWorkItem>(w =>
            w.Type == IngestionWorkType.IngestEntities && w.BookId == 1));
    }
}