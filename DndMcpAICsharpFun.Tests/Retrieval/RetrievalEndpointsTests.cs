using System.Net;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Retrieval;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DndMcpAICsharpFun.Tests.Retrieval;

public sealed class RetrievalEndpointsTests : IAsyncLifetime
{
    private const string ValidApiKey = "test-key";

    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private IRagRetrievalService _retrievalService = null!;

    private static ChunkMetadata MakeMetadata() => new(
        SourceBook: "PHB",
        Version: DndVersion.Edition2024,
        Category: ContentCategory.Spell,
        EntityName: "Fireball",
        Chapter: "Spells",
        PageNumber: 42,
        ChunkIndex: 0);

    public async Task InitializeAsync()
    {
        _retrievalService = Substitute.For<IRagRetrievalService>();

        _retrievalService
            .SearchAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<RetrievalResult>>(
                [new RetrievalResult("Some text", MakeMetadata(), 0.9f)]));

        _retrievalService
            .SearchDiagnosticAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<RetrievalDiagnosticResult>>(
                [new RetrievalDiagnosticResult("Some text", MakeMetadata(), 0.9f, "point-1")]));

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(_retrievalService);
        builder.Services.Configure<AdminOptions>(o => o.ApiKey = ValidApiKey);

        _app = builder.Build();

        _app.UseWhen(
            ctx => ctx.Request.Path.StartsWithSegments("/admin"),
            adminApp => adminApp.UseMiddleware<AdminApiKeyMiddleware>()
        );

        _app.MapRetrievalEndpoints();

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    // 6.2 — no q parameter returns 400
    [Fact]
    public async Task Search_NoQueryParam_Returns400()
    {
        var response = await _client.GetAsync("/retrieval/search");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // 6.3 — whitespace q returns 400
    [Fact]
    public async Task Search_WhitespaceQuery_Returns400()
    {
        var response = await _client.GetAsync("/retrieval/search?q=   ");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // 6.4 — valid q returns 200 and calls SearchAsync once
    [Fact]
    public async Task Search_ValidQuery_Returns200AndCallsSearchAsync()
    {
        _retrievalService.ClearReceivedCalls();

        var response = await _client.GetAsync("/retrieval/search?q=fireball");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(body);

        await _retrievalService.Received(1).SearchAsync(
            Arg.Any<RetrievalQuery>(),
            Arg.Any<CancellationToken>());
    }

    // 6.5 — valid version and category are parsed and passed to SearchAsync
    [Fact]
    public async Task Search_ValidVersionAndCategory_ParsedAndPassedToSearchAsync()
    {
        _retrievalService.ClearReceivedCalls();

        var response = await _client.GetAsync("/retrieval/search?q=fireball&version=Edition2024&category=Spell");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _retrievalService.Received(1).SearchAsync(
            Arg.Is<RetrievalQuery>(q =>
                q.QueryText == "fireball" &&
                q.Version == DndVersion.Edition2024 &&
                q.Category == ContentCategory.Spell),
            Arg.Any<CancellationToken>());
    }

    // 6.6 — invalid version and category result in null passed to SearchAsync
    [Fact]
    public async Task Search_InvalidVersionAndCategory_NullPassedToSearchAsync()
    {
        _retrievalService.ClearReceivedCalls();

        var response = await _client.GetAsync("/retrieval/search?q=fireball&version=invalid&category=invalid");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _retrievalService.Received(1).SearchAsync(
            Arg.Is<RetrievalQuery>(q =>
                q.QueryText == "fireball" &&
                q.Version == null &&
                q.Category == null),
            Arg.Any<CancellationToken>());
    }

    // 6.7 — admin search without API key returns 401
    [Fact]
    public async Task AdminSearch_NoApiKey_Returns401()
    {
        _retrievalService.ClearReceivedCalls();

        var response = await _client.GetAsync("/admin/retrieval/search?q=fireball");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await _retrievalService.DidNotReceive().SearchDiagnosticAsync(Arg.Any<RetrievalQuery>(), Arg.Any<CancellationToken>());
    }

    // 6.8 — admin search with valid API key returns 200 and calls SearchDiagnosticAsync once
    [Fact]
    public async Task AdminSearch_ValidApiKey_Returns200AndCallsSearchDiagnosticAsync()
    {
        _retrievalService.ClearReceivedCalls();

        var request = new HttpRequestMessage(HttpMethod.Get, "/admin/retrieval/search?q=fireball");
        request.Headers.Add("X-Admin-Api-Key", ValidApiKey);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await _retrievalService.Received(1).SearchDiagnosticAsync(
            Arg.Any<RetrievalQuery>(),
            Arg.Any<CancellationToken>());
    }
}
