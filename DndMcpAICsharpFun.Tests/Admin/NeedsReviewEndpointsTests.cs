using System.Net;

using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;

using FluentAssertions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace DndMcpAICsharpFun.Tests.Admin;

/// <summary>
/// F6 — Admin endpoint binding + auth integration smoke.
///
/// Covers:
///   1. Optional paging defaults: GET /admin/entities/needs-review with no offset/limit
///      must return 200 (not 400).  This is the regression that previously bit us when
///      the params were accidentally required.
///   2. Admin-key auth enforcement: a request without the X-Admin-Api-Key header
///      must return 401; with the correct key it must not be 401.
///
/// Uses a minimal WebApplication (same pattern as BooksAdminEndpointsTests) rather
/// than the full Program host.  Only the services actually needed by
/// NeedsReviewEndpoints are registered, with NSubstitute fakes for NeedsReviewService's
/// dependencies so no real Qdrant, DB, or file I/O is involved.
/// </summary>
public sealed class NeedsReviewEndpointsTests
{
    private const string ValidKey = "test-admin-key";

    // ── Factory ───────────────────────────────────────────────────────────────

    private static async Task<HttpClient> BuildClientAsync(
        NeedsReviewService? svcOverride = null)
    {
        var svc = svcOverride ?? BuildFakeNeedsReviewService();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddSingleton(svc);

        // AdminApiKeyMiddleware reads AdminOptions.ApiKey.
        builder.Services.Configure<AdminOptions>(o => o.ApiKey = ValidKey);

        var app = builder.Build();

        // Wire AdminApiKeyMiddleware on /admin paths — mirrors MapAdminMiddleware().
        app.UseWhen(
            static ctx => ctx.Request.Path.StartsWithSegments("/admin"),
            static branch => branch.UseMiddleware<AdminApiKeyMiddleware>());

        app.MapGroup("/admin").MapNeedsReview();

        await app.StartAsync();
        return app.GetTestClient();
    }

    private static NeedsReviewService BuildFakeNeedsReviewService()
    {
        // CanonicalJsonLoader and CanonicalJsonWriter are concrete classes without
        // virtual methods — use real instances.  NeedsReviewService gracefully handles
        // a non-existent canonical directory (GetCanonicalFiles returns []), so no
        // real file I/O occurs during these smoke tests.
        var loader = new CanonicalJsonLoader();
        var writer = new CanonicalJsonWriter();
        var orchestrator = Substitute.For<IEntityIngestionOrchestrator>();
        var tracker = Substitute.For<IIngestionTracker>();
        var options = Microsoft.Extensions.Options.Options.Create(
            new EntityExtractionOptions
            {
                // Point at a non-existent dir so GetCanonicalFiles() returns [] immediately.
                CanonicalDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()),
            });

        return new NeedsReviewService(loader, writer, orchestrator, tracker, options);
    }

    // ── Tests: auth ───────────────────────────────────────────────────────────

    [Fact]
    public async Task NeedsReview_WithoutApiKey_Returns401()
    {
        var client = await BuildClientAsync();

        var response = await client.GetAsync("/admin/entities/needs-review");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the AdminApiKeyMiddleware must reject requests without X-Admin-Api-Key");
    }

    [Fact]
    public async Task NeedsReview_WithWrongApiKey_Returns401()
    {
        var client = await BuildClientAsync();
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", "wrong-key");

        var response = await client.GetAsync("/admin/entities/needs-review");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the AdminApiKeyMiddleware must reject requests with the wrong key");
    }

    [Fact]
    public async Task NeedsReview_WithCorrectApiKey_IsNotUnauthorized()
    {
        var client = await BuildClientAsync();
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", ValidKey);

        var response = await client.GetAsync("/admin/entities/needs-review");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized,
            "a request with the correct key must pass the auth middleware");
    }

    // ── Tests: optional paging defaults ───────────────────────────────────────

    /// <summary>
    /// Regression: offset and limit are optional int? parameters.  Omitting them
    /// must default to offset=0, limit=50 and return 200 — not 400.
    /// </summary>
    [Fact]
    public async Task NeedsReview_NoOffsetOrLimit_Returns200()
    {
        var client = await BuildClientAsync();
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", ValidKey);

        var response = await client.GetAsync("/admin/entities/needs-review");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "offset and limit are optional parameters with defaults — omitting them must not produce 400");
    }

    [Fact]
    public async Task NeedsReview_WithExplicitPaging_Returns200()
    {
        var client = await BuildClientAsync();
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", ValidKey);

        var response = await client.GetAsync("/admin/entities/needs-review?offset=0&limit=10");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "explicitly supplying valid offset/limit must also return 200");
    }

    [Fact]
    public async Task NeedsReview_EmptyCanonicalDir_ReturnsEmptyList()
    {
        var client = await BuildClientAsync();
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", ValidKey);

        var response = await client.GetAsync("/admin/entities/needs-review");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // The response envelope contains items + total; an empty dir yields total=0.
        body.Should().Contain("\"total\":0",
            "when no canonical files exist the response should report zero items");
    }

    // ── Tests: GET /admin/entities/{id} ───────────────────────────────────────

    [Fact]
    public async Task GetEntityById_WithCorrectKey_Returns404ForUnknownId()
    {
        var client = await BuildClientAsync();
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", ValidKey);

        var response = await client.GetAsync("/admin/entities/unknown-entity-id");

        // The fake service has no canonical files, so the entity won't be found.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetEntityById_WithoutKey_Returns401()
    {
        var client = await BuildClientAsync();

        var response = await client.GetAsync("/admin/entities/some-id");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}