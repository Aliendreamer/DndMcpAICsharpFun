using System.Net;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.Programmatic;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DndMcpAICsharpFun.Tests.Programmatic;

public class ExtractProgrammaticEndpointTests
{
    private static async Task<(HttpClient Client, IIngestionTracker Tracker, IProgrammaticExtractionQueue Queue)> BuildAsync()
    {
        var tracker = Substitute.For<IIngestionTracker>();
        var queue = Substitute.For<IProgrammaticExtractionQueue>();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(tracker);
        builder.Services.AddSingleton(queue);
        builder.Services.AddSingleton(Substitute.For<IIngestionQueue>());
        builder.Services.AddSingleton(Substitute.For<IBookDeletionService>());
        var app = builder.Build();
        app.MapGroup("/admin").MapBooksAdmin();
        await app.StartAsync();
        return (app.GetTestClient(), tracker, queue);
    }

    [Fact]
    public async Task Endpoint_202_404_409()
    {
        var (client, tracker, queue) = await BuildAsync();
        tracker.GetByIdAsync(404, Arg.Any<CancellationToken>()).Returns((IngestionRecord?)null);
        tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new IngestionRecord { Id = 1, FivetoolsSourceKey = "MM" });
        tracker.GetByIdAsync(2, Arg.Any<CancellationToken>()).Returns(new IngestionRecord { Id = 2, FivetoolsSourceKey = "PHB" });
        queue.TryEnqueue(1).Returns(true);
        queue.TryEnqueue(2).Returns(false);

        var notFound = await client.PostAsync("/admin/books/404/extract-programmatic", null);
        var accepted = await client.PostAsync("/admin/books/1/extract-programmatic", null);
        var conflict = await client.PostAsync("/admin/books/2/extract-programmatic", null);

        notFound.StatusCode.Should().Be(HttpStatusCode.NotFound);
        accepted.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await accepted.Content.ReadAsStringAsync()).Should().Contain("\"bookId\":1");
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData("XDMG", null)]
    [InlineData(null, "Some Homebrew Book")]
    [InlineData("XPHB", null)]
    public async Task Endpoint_400_for_books_outside_the_allow_list(string? key, string? displayName)
    {
        var (client, tracker, queue) = await BuildAsync();
        tracker.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(new IngestionRecord
        {
            Id = 7, FivetoolsSourceKey = key, DisplayName = displayName ?? "",
        });
        queue.TryEnqueue(7).Returns(true);

        var res = await client.PostAsync("/admin/books/7/extract-programmatic", null);

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await res.Content.ReadAsStringAsync()).Should().Contain("phb14").And.Contain("only enabled for");
        queue.DidNotReceive().TryEnqueue(Arg.Any<int>());
    }

    [Theory]
    [InlineData("PHB", null)]
    [InlineData("MM", null)]
    [InlineData("DMG", null)]
    [InlineData("XGE", null)]
    [InlineData("ERLW", null)]
    [InlineData("TCE", null)]
    [InlineData(null, "Elemental Evil Player's Companion")]
    public async Task Endpoint_accepts_the_validated_books(string? key, string? displayName)
    {
        var (client, tracker, queue) = await BuildAsync();
        tracker.GetByIdAsync(3, Arg.Any<CancellationToken>()).Returns(new IngestionRecord
        {
            Id = 3, FivetoolsSourceKey = key, DisplayName = displayName ?? "",
        });
        queue.TryEnqueue(3).Returns(true);

        var res = await client.PostAsync("/admin/books/3/extract-programmatic", null);

        res.StatusCode.Should().Be(HttpStatusCode.Accepted);
    }
}
