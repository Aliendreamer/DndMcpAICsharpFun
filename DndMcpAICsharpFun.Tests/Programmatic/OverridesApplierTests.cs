using System.Net;
using System.Text.Json;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.Programmatic;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Tests.Programmatic;

public class OverridesApplierTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "apply-" + Guid.NewGuid().ToString("N"));
    private readonly IIngestionTracker _tracker = Substitute.For<IIngestionTracker>();
    private readonly IEntityIngestionOrchestrator _ingestion = Substitute.For<IEntityIngestionOrchestrator>();
    private readonly IOptions<EntityIngestionOptions> _opts;
    private static readonly DateTimeOffset At = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public OverridesApplierTests()
    {
        Directory.CreateDirectory(_dir);
        _opts = Options.Create(new EntityIngestionOptions { CanonicalDirectory = _dir });
        _tracker.GetByIdAsync(1, Arg.Any<CancellationToken>()).Returns(new IngestionRecord
        {
            Id = 1, FileHash = "h", FivetoolsSourceKey = "MM", DisplayName = "Monster Manual",
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    private static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private OverridesApplier Applier() => new(_tracker, new CanonicalJsonLoader(), new CanonicalJsonWriter(),
        new OverridesStore(_opts), _ingestion, new CanonicalBookLocks(), _opts);

    private static EntityEnvelope Ent(string id, bool pass, string cr = "2") => new(
        id, EntityType.Monster, id[(id.LastIndexOf('.') + 1)..], "MM", "Edition2014", 1,
        new FirstAppearance("MM", "Edition2014", 1), [], [], "text", J($$"""{"cr":"{{cr}}","size":["M"],"type":"beast"}"""),
        DataSource: DataSources.Programmatic, Authority: "book",
        Checks: [new EntityCheck("stat-block-tail", pass, pass ? "" : "tail")],
        Disposition: pass ? EntityDisposition.Accepted : EntityDisposition.NeedsReview, NeedsReview: !pass);

    private async Task SeedAsync(params EntityEnvelope[] ents) =>
        await new CanonicalJsonWriter().WriteAsync(Path.Combine(_dir, "mm14.json"),
            new CanonicalJsonFile(CanonicalJsonSchema.CurrentVersion,
                new CanonicalBookMetadata("MM", "Edition2014", "h", "Monster Manual"), ents), default);

    [Fact]
    public async Task Applies_override_and_approval_to_the_current_file_then_reingests()
    {
        await SeedAsync(Ent("mm14.monster.a", true), Ent("mm14.monster.b", false));
        await new OverridesStore(_opts).SaveAsync("mm14", new EntityOverrides(
            [new FieldOverride("mm14.monster.b", "fields.cr", J("\"5\""), J("\"2\""), "verified against 5etools", At)],
            [new Approval("mm14.monster.b", "verified against 5etools", At, ["stat-block-tail"])]), default);

        var r = await Applier().ApplyAsync(1, default);

        r.Should().Be(new ApplyOverridesResult(1, 2, 0));
        var file = await new CanonicalJsonLoader().LoadAsync(Path.Combine(_dir, "mm14.json"), default);
        var b = file.Entities.Single(e => e.Id == "mm14.monster.b");
        b.Fields.GetProperty("cr").GetString().Should().Be("5");
        b.Disposition.Should().Be(EntityDisposition.Accepted);
        await _ingestion.Received(1).IngestEntitiesAsync(1, Arg.Any<CancellationToken>());

        // running it again changes nothing
        (await Applier().ApplyAsync(1, default)).Should().Be(r);
    }

    [Fact]
    public async Task Unknown_book_is_null_and_missing_or_legacy_file_throws()
    {
        (await Applier().ApplyAsync(404, default)).Should().BeNull();
        await FluentActions.Awaiting(() => Applier().ApplyAsync(1, default)).Should().ThrowAsync<NoProgrammaticFileException>();
        File.WriteAllText(Path.Combine(_dir, "mm14.json"), """{"schemaVersion":"1","book":{"sourceBook":"MM","edition":"Edition2014"},"entities":[]}""");
        await FluentActions.Awaiting(() => Applier().ApplyAsync(1, default)).Should().ThrowAsync<Exception>();
        await _ingestion.DidNotReceive().IngestEntitiesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Endpoint_200_404_409()
    {
        var applier = Substitute.For<IOverridesApplier>();
        applier.ApplyAsync(1, Arg.Any<CancellationToken>()).Returns(new ApplyOverridesResult(3, 10, 2));
        applier.ApplyAsync(404, Arg.Any<CancellationToken>()).Returns((ApplyOverridesResult?)null);
        applier.ApplyAsync(9, Arg.Any<CancellationToken>()).Returns<ApplyOverridesResult?>(_ => throw new NoProgrammaticFileException("none"));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(applier);
        builder.Services.AddSingleton(Substitute.For<IIngestionTracker>());
        builder.Services.AddSingleton(Substitute.For<IProgrammaticExtractionQueue>());
        builder.Services.AddSingleton(Substitute.For<DndMcpAICsharpFun.Features.Ingestion.IIngestionQueue>());
        builder.Services.AddSingleton(Substitute.For<DndMcpAICsharpFun.Features.Ingestion.IBookDeletionService>());
        var app = builder.Build();
        app.MapGroup("/admin").MapBooksAdmin();
        await app.StartAsync();
        var client = app.GetTestClient();

        var ok = await client.PostAsync("/admin/books/1/apply-overrides", null);
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ok.Content.ReadAsStringAsync()).Should().Be("""{"bookId":1,"applied":3,"accepted":10,"needsReview":2}""");
        (await client.PostAsync("/admin/books/404/apply-overrides", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostAsync("/admin/books/9/apply-overrides", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
