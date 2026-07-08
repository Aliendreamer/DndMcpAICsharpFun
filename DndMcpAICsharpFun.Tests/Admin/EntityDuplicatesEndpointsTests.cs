using System.Net;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Retrieval.Entities.Dedup;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using DndMcpAICsharpFun.Tests.Retrieval.Entities.Dedup;
using DndMcpAICsharpFun.Tests.TestDoubles;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Admin;

/// <summary>
/// Admin endpoint binding + auth integration smoke for the entity-dedup routes.
///
/// Covers:
///   1. Admin-key auth enforcement: a request without X-Admin-Api-Key must return 401.
///   2. GET /admin/retrieval/entities/duplicates returns 200 with a DuplicateReport body.
///   3. POST /admin/retrieval/entities/compact (no apply) returns 200 and deletes nothing.
///   4. POST /admin/retrieval/entities/compact?apply=true returns 200 and deletes the loser.
///
/// Uses a minimal WebApplication (same pattern as NeedsReviewEndpointsTests /
/// BooksAdminEndpointsTests) with AdminApiKeyMiddleware wired manually, and a real
/// EntityDuplicateService backed by the in-memory RecordingEntityVectorStore fake
/// (reusing the seeding helpers from EntityDuplicateServiceTests) so the dry-run vs.
/// apply distinction can be asserted end-to-end through the HTTP layer.
/// </summary>
public sealed class EntityDuplicatesEndpointsTests
{
    private const string ValidKey = "test-admin-key";

    private sealed class FakeBookTypeLookup : IBookTypeLookup
    {
        public Task<IReadOnlyDictionary<string, BookType>> BuildAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, BookType>>(new Dictionary<string, BookType>
            {
                ["PHB"] = BookType.Core,
                ["XGE"] = BookType.Supplement,
            });
    }

    private static async Task<(HttpClient Client, RecordingEntityVectorStore Store, string PhbId, string XgeId)>
        BuildClientAsync()
    {
        var store = new RecordingEntityVectorStore();
        var phb = TestEnvelopes.Make("phb.spell.fireball", "Fireball", EntityType.Spell, "Edition2014", sourceBook: "PHB");
        var xge = TestEnvelopes.Make("xge.spell.fireball", "Fireball", EntityType.Spell, "Edition2014", sourceBook: "XGE");

        await store.UpsertAsync(
        [
            new EntityPoint(phb, [0f], "hash-phb"),
            new EntityPoint(xge, [0f], "hash-xge"),
        ]);

        var svc = new EntityDuplicateService(store, new FakeBookTypeLookup());

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

        app.MapGroup("/admin").MapRetrievalAdmin();

        await app.StartAsync();
        return (app.GetTestClient(), store, phb.Id, xge.Id);
    }

    // ── Tests: auth ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Duplicates_WithoutApiKey_Returns401()
    {
        var (client, _, _, _) = await BuildClientAsync();

        var response = await client.GetAsync("/admin/retrieval/entities/duplicates");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the AdminApiKeyMiddleware must reject requests without X-Admin-Api-Key");
    }

    [Fact]
    public async Task Compact_WithoutApiKey_Returns401()
    {
        var (client, _, _, _) = await BuildClientAsync();

        var response = await client.PostAsync("/admin/retrieval/entities/compact", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the AdminApiKeyMiddleware must reject requests without X-Admin-Api-Key");
    }

    // ── Tests: GET duplicates ────────────────────────────────────────────────

    [Fact]
    public async Task GetDuplicates_WithApiKey_Returns200WithReport()
    {
        var (client, _, phbId, xgeId) = await BuildClientAsync();
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", ValidKey);

        var response = await client.GetAsync("/admin/retrieval/entities/duplicates");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"groupCount\":1");
        body.Should().Contain(phbId);
        body.Should().Contain(xgeId);
    }

    // ── Tests: POST compact ──────────────────────────────────────────────────

    [Fact]
    public async Task Compact_NoApply_Returns200_AndDeletesNothing()
    {
        var (client, store, phbId, xgeId) = await BuildClientAsync();
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", ValidKey);

        var response = await client.PostAsync("/admin/retrieval/entities/compact", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        store.Ids.Should().Contain([phbId, xgeId],
            "apply defaults to false — compact should report but not delete");
    }

    [Fact]
    public async Task Compact_ApplyTrue_Returns200_AndDeletesLoser()
    {
        var (client, store, phbId, xgeId) = await BuildClientAsync();
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", ValidKey);

        var response = await client.PostAsync("/admin/retrieval/entities/compact?apply=true", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        store.Ids.Should().Contain(phbId);
        store.Ids.Should().NotContain(xgeId);
    }
}
