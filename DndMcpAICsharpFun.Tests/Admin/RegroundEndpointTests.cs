using System.Net;
using System.Text.Json;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Infrastructure.Ingestion;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Qdrant.Client.Grpc;
using DomainSparseVector = DndMcpAICsharpFun.Infrastructure.Search.SparseVector;

namespace DndMcpAICsharpFun.Tests.Admin;

/// <summary>
/// Endpoint smoke tests for POST /admin/books/{id}/reground-entities.
///
/// Reuses the exact app-bootstrap from <see cref="BooksAdminEndpointsTests"/> — because
/// <c>MapBooksAdmin()</c> maps every sibling route in the same group, and ASP.NET Core's
/// RequestDelegateFactory infers endpoint metadata for the *whole* group up front, every
/// sibling handler's DI dependencies (tracker, queue, deletionService, BookSourceRegistry,
/// etc.) must be registered even though this test only ever calls reground-entities —
/// otherwise metadata inference throws ("Body was inferred but the method does not allow
/// inferred body parameters") for unrelated GET routes like GetAllBooks. On top of that
/// harness, AdminApiKeyMiddleware is wired manually (mirrors NeedsReviewEndpointsTests /
/// EntityDuplicatesEndpointsTests) so the missing-key rejection can be asserted, and a real
/// <see cref="RegroundService"/> is constructed with NSubstitute/fake dependencies (same
/// fakes as <see cref="RegroundServiceTests"/>) so the endpoint test exercises the real
/// service end-to-end without any actual Qdrant, LLM, or DB dependency.
/// </summary>
public sealed class RegroundEndpointTests : IDisposable
{
    private const string ValidKey = "test-admin-key";
    private const string BookSlug = "reground-endpoint-book";

    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "reground-endpoint-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _booksDir =
        Path.Combine(Path.GetTempPath(), "reground-endpoint-tests-books-" + Guid.NewGuid().ToString("N"));

    public RegroundEndpointTests()
    {
        Directory.CreateDirectory(_dir);
        Directory.CreateDirectory(_booksDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
        if (Directory.Exists(_booksDir))
            Directory.Delete(_booksDir, recursive: true);
    }

    // ── Fakes (mirrors RegroundServiceTests) ────────────────────────────────────

    private sealed class FakeGroundingCascade : IGroundingCascade
    {
        public bool? LastJudgeEnabled { get; private set; }

        public Task<GroundingVerdict> GradeAsync(
            EntityEnvelope entity, string sourceProse, bool judgeEnabled, CancellationToken ct)
        {
            LastJudgeEnabled = judgeEnabled;
            var verdict = judgeEnabled
                ? new GroundingVerdict(GroundingStatus.Ungrounded, DecidedByTier: 2, Score: 0.1)
                : new GroundingVerdict(GroundingStatus.Grounded, DecidedByTier: 0, Score: 1.0);
            return Task.FromResult(verdict);
        }
    }

    private sealed class FakeEmbeddingService : IEmbeddingService
    {
        public Task<IList<float[]>> EmbedAsync(IList<string> texts, CancellationToken ct = default) =>
            Task.FromResult<IList<float[]>>(texts.Select(_ => new float[] { 0.1f, 0.2f }).ToList());
    }

    private sealed class FakeQdrantSearchClient : IQdrantSearchClient
    {
        public Task<IReadOnlyList<ScoredPoint>> SearchAsync(
            string collectionName,
            ReadOnlyMemory<float> vector,
            Filter? filter = null,
            ulong limit = 10,
            float? scoreThreshold = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ScoredPoint>>([]);

        public Task<IReadOnlyList<ScoredPoint>> QueryAsync(
            string collectionName,
            ReadOnlyMemory<float> denseVector,
            DomainSparseVector sparseVector,
            Filter? filter = null,
            ulong limit = 10,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("RegroundService does not use hybrid query.");
    }

    private static EntityEnvelope MakeEntity(string id, string name) => new(
        Id: id,
        Type: EntityType.Spell,
        Name: name,
        SourceBook: BookSlug,
        Edition: "Edition2014",
        Page: 10,
        FirstAppearedIn: new FirstAppearance(BookSlug, "Edition2014", 10),
        RevisedIn: [],
        SettingTags: [],
        CanonicalText: $"{name} canonical text.",
        Fields: JsonDocument.Parse("{}").RootElement.Clone(),
        NeedsReview: true,
        Disposition: EntityDisposition.NeedsReview);

    // ── Factory ───────────────────────────────────────────────────────────────

    private async Task<(HttpClient Client, FakeGroundingCascade Cascade)> BuildClientAsync()
    {
        var cascade = new FakeGroundingCascade();

        var tracker = Substitute.For<IIngestionTracker>();
        var record = new IngestionRecord
        {
            Id = 1, DisplayName = BookSlug,
            FilePath = "/tmp/fake.pdf", FileName = "fake.pdf",
            FileHash = "cafebabe", Version = "Edition2014",
            Status = IngestionStatus.EntitiesIngested,
        };
        tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(record);

        var orchestrator = Substitute.For<IEntityIngestionOrchestrator>();
        orchestrator.ReindexEntityAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var loader = new CanonicalJsonLoader();
        var writer = new CanonicalJsonWriter();

        var entity = MakeEntity($"{BookSlug}.spell.flagged-one", "Flagged One");
        var canonicalFile = new CanonicalJsonFile(
            SchemaVersion: CanonicalJsonSchema.CurrentVersion,
            Book: new CanonicalBookMetadata(BookSlug, "Edition2014", "cafebabe", BookSlug),
            Entities: [entity]);
        await writer.WriteAsync(Path.Combine(_dir, BookSlug + ".json"), canonicalFile, CancellationToken.None);

        var svc = new RegroundService(
            loader,
            writer,
            orchestrator,
            tracker,
            cascade,
            new FakeEmbeddingService(),
            new FakeQdrantSearchClient(),
            Options.Create(new QdrantOptions { BlocksCollectionName = "dnd_blocks" }),
            Options.Create(new GroundingOptions()),
            Options.Create(new EntityExtractionOptions { CanonicalDirectory = _dir }));

        // Fakes for sibling MapBooksAdmin() routes' dependencies — unused by this test's
        // requests, but must be registered so RequestDelegateFactory's up-front metadata
        // inference for the whole group (GetAllBooks, IngestBlocks, etc.) doesn't throw.
        var queue = Substitute.For<IIngestionQueue>();
        var deletionService = Substitute.For<IBookDeletionService>();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(svc);
        builder.Services.AddSingleton(tracker);
        builder.Services.AddSingleton(queue);
        builder.Services.AddSingleton(deletionService);
        builder.Services.AddSingleton(
            new BookSourceRegistry(TestPaths.RepoFile("5etools/books.json")));
        builder.Services.AddSingleton<ILogger<BookRegistrationService>>(
            NullLogger<BookRegistrationService>.Instance);
        builder.Services.AddScoped<BookRegistrationService>();
        builder.Services.Configure<IngestionOptions>(o => o.BooksPath = _booksDir);
        builder.Services.Configure<EntityExtractionOptions>(o => o.CanonicalDirectory = _dir);

        // AdminApiKeyMiddleware reads AdminOptions.ApiKey.
        builder.Services.Configure<AdminOptions>(o => o.ApiKey = ValidKey);

        var app = builder.Build();

        // Wire AdminApiKeyMiddleware on /admin paths — mirrors MapAdminMiddleware().
        app.UseWhen(
            static ctx => ctx.Request.Path.StartsWithSegments("/admin"),
            static branch => branch.UseMiddleware<AdminApiKeyMiddleware>());

        app.MapGroup("/admin").MapBooksAdmin();

        await app.StartAsync();
        return (app.GetTestClient(), cascade);
    }

    // ── Tests: auth ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Reground_WithoutApiKey_Returns401()
    {
        var (client, _) = await BuildClientAsync();

        var response = await client.PostAsync("/admin/books/1/reground-entities", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the AdminApiKeyMiddleware must reject requests without X-Admin-Api-Key");
    }

    // ── Tests: happy path ─────────────────────────────────────────────────────

    [Fact]
    public async Task Reground_WithApiKey_Returns200AndRegroundResult()
    {
        var (client, cascade) = await BuildClientAsync();
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", ValidKey);

        var response = await client.PostAsync("/admin/books/1/reground-entities", null);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        cascade.LastJudgeEnabled.Should().BeFalse("judge defaults to false when the query param is absent");

        var result = JsonSerializer.Deserialize<RegroundResult>(body, JsonSerializerOptions.Web);
        result.Should().NotBeNull();
        result!.Scanned.Should().Be(1);
        result.Promoted.Should().Be(1);
        result.Tier2Invoked.Should().Be(0, "the fake cascade only escalates to tier 2 when judge=true");
    }

    [Fact]
    public async Task Reground_JudgeTrue_ReachesServiceAndInvokesTier2()
    {
        var (client, cascade) = await BuildClientAsync();
        client.DefaultRequestHeaders.Add("X-Admin-Api-Key", ValidKey);

        var response = await client.PostAsync("/admin/books/1/reground-entities?judge=true", null);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        cascade.LastJudgeEnabled.Should().BeTrue("?judge=true must reach RegroundService.RegroundAsync's judge parameter");

        var result = JsonSerializer.Deserialize<RegroundResult>(body, JsonSerializerOptions.Web);
        result.Should().NotBeNull();
        result!.Tier2Invoked.Should().Be(1, "the fake cascade escalates to tier 2 whenever judge=true");
    }
}
