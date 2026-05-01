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

public sealed class CancelExtractEndpointTests
{
    private static async Task<(HttpClient Client, IExtractionCancellationRegistry Registry)> BuildClientAsync()
    {
        var registry = Substitute.For<IExtractionCancellationRegistry>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        // Register all services that BooksAdminEndpoints handlers depend on
        builder.Services.AddSingleton<IExtractionCancellationRegistry>(registry);
        builder.Services.AddSingleton<IIngestionTracker>(Substitute.For<IIngestionTracker>());
        builder.Services.AddSingleton<IIngestionQueue>(Substitute.For<IIngestionQueue>());
        builder.Services.AddSingleton<IIngestionOrchestrator>(Substitute.For<IIngestionOrchestrator>());
        builder.Services.AddSingleton<IEntityJsonStore>(Substitute.For<IEntityJsonStore>());
        builder.Services.AddSingleton<ILogger<RegisterBookRequest>>(NullLogger<RegisterBookRequest>.Instance);
        builder.Services.AddSingleton<ILogger<RegisterBookByPathRequest>>(NullLogger<RegisterBookByPathRequest>.Instance);
        builder.Services.Configure<AdminOptions>(o => o.ApiKey = "test-key");
        builder.Services.Configure<IngestionOptions>(o => o.BooksPath = Path.GetTempPath());
        var app = builder.Build();
        app.UseWhen(
            ctx => ctx.Request.Path.StartsWithSegments("/admin"),
            adminApp => adminApp.UseMiddleware<AdminApiKeyMiddleware>()
        );
        app.MapGroup("/admin").MapBooksAdmin();

        await app.StartAsync();

        var client = app.GetTestClient();
        return (client, registry);
    }

    [Fact]
    public async Task CancelExtract_Returns200_WhenCancelReturnsTrue()
    {
        var (client, registry) = await BuildClientAsync();
        registry.Cancel(1).Returns(true);

        var request = new HttpRequestMessage(HttpMethod.Post, "/admin/books/1/cancel-extract");
        request.Headers.Add("X-Admin-Api-Key", "test-key");

        var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        registry.Received(1).Cancel(1);
    }

    [Fact]
    public async Task CancelExtract_Returns404_WhenCancelReturnsFalse()
    {
        var (client, registry) = await BuildClientAsync();
        registry.Cancel(2).Returns(false);

        var request = new HttpRequestMessage(HttpMethod.Post, "/admin/books/2/cancel-extract");
        request.Headers.Add("X-Admin-Api-Key", "test-key");

        var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        registry.Received(1).Cancel(2);
    }

    [Fact]
    public async Task CancelExtract_Returns401_WhenAdminKeyMissing()
    {
        var (client, registry) = await BuildClientAsync();

        var request = new HttpRequestMessage(HttpMethod.Post, "/admin/books/1/cancel-extract");
        // No X-Admin-Api-Key header

        var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        registry.DidNotReceive().Cancel(Arg.Any<int>());
    }

    [Fact]
    public async Task CancelExtract_Returns401_WhenAdminKeyInvalid()
    {
        var (client, registry) = await BuildClientAsync();

        var request = new HttpRequestMessage(HttpMethod.Post, "/admin/books/1/cancel-extract");
        request.Headers.Add("X-Admin-Api-Key", "wrong-key");

        var response = await client.SendAsync(request);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        registry.DidNotReceive().Cancel(Arg.Any<int>());
    }
}
