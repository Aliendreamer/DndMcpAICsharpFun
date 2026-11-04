using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using DndMcpAICsharpFun.Infrastructure.Ingestion;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities;

public class ProgrammaticIngestionTests
{
    private const string Id = "progbook.feat.alert";

    private static async Task<(IList<EntityPoint>? Captured, IEntityVectorStore Store)> RunAsync(
        string dataSource, string disposition, EntityEnvelope? existing = null)
    {
        var record = new IngestionRecord { Id = 7, DisplayName = $"Prog Book {Guid.NewGuid():N}", FileHash = "hash" };
        var dir = Path.Combine(Path.GetTempPath(), $"prog-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var slug = EntityIdSlug.BookSlug(record);
        var ds = dataSource.Length > 0 ? $"\"dataSource\": \"{dataSource}\"," : "";
        await File.WriteAllTextAsync(Path.Combine(dir, slug + ".json"), $$"""
            {
              "schemaVersion": "2",
              "book": { "sourceBook": "Prog", "edition": "Edition2014", "fileHash": "x", "displayName": "Prog" },
              "entities": [{
                "id": "{{Id}}", "type": "Feat", "name": "Alert",
                "sourceBook": "Prog", "edition": "Edition2014", "page": 1,
                {{ds}}
                "disposition": "{{disposition}}",
                "firstAppearedIn": { "book": "Prog", "edition": "Edition2014", "page": 1 },
                "revisedIn": [], "settingTags": [], "canonicalText": "", "fields": {}
              }]
            }
            """);

        var tracker = Substitute.For<IIngestionTracker>();
        tracker.GetByIdAsync(7, Arg.Any<CancellationToken>()).Returns(record);
        var embeddings = Substitute.For<IEmbeddingService>();
        embeddings.EmbedAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IList<float[]>>(
                ci.Arg<IList<string>>().Select(_ => new float[1024]).ToList()));
        IList<EntityPoint>? captured = null;
        var store = Substitute.For<IEntityVectorStore>();
        store.GetByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(existing is null
                ? new Dictionary<string, EntityEnvelope>()
                : new Dictionary<string, EntityEnvelope> { [Id] = existing });
        store.UpsertAsync(Arg.Any<IList<EntityPoint>>(), Arg.Any<CancellationToken>())
            .Returns(ci => { captured = ci.Arg<IList<EntityPoint>>(); return Task.CompletedTask; });

        try
        {
            var orchestrator = new EntityIngestionOrchestrator(
                tracker, new CanonicalJsonLoader(), new EntityCanonicalTextDispatcher(),
                new EntityReferenceResolver(), embeddings, store,
                Options.Create(new EntityIngestionOptions
                    { CanonicalDirectory = dir, FivetoolsDirectory = Path.Combine(dir, "none") }),
                NullLogger<EntityIngestionOrchestrator>.Instance);
            await orchestrator.IngestEntitiesAsync(7, CancellationToken.None);
        }
        finally { Directory.Delete(dir, true); }
        return (captured, store);
    }

    private static EntityEnvelope Existing(bool srd) => new(
        Id, EntityType.Feat, "Alert", "Prog", "Edition2014", 1,
        new FirstAppearance("Prog", "Edition2014"), [], [], "", default,
        DataSource: "5etools", Srd: srd);

    [Fact]
    public async Task Programmatic_NeedsReview_not_ingested()
    {
        var (captured, _) = await RunAsync("programmatic", "NeedsReview");
        (captured ?? []).Should().BeEmpty();
    }

    [Fact]
    public async Task Programmatic_Accepted_ingested_without_fivetools_merge()
    {
        // Existing record claims Srd=true; the programmatic envelope says false and must win.
        var (captured, _) = await RunAsync("programmatic", "Accepted", Existing(srd: true));
        var p = captured.Should().NotBeNull().And.ContainSingle().Subject;
        p.Envelope.Srd.Should().BeFalse();
        p.Envelope.DataSource.Should().Be(DataSources.Programmatic);
    }

    [Fact]
    public async Task NonProgrammatic_NeedsReview_still_ingested()
    {
        var (captured, _) = await RunAsync("", "NeedsReview");
        captured.Should().NotBeNull().And.ContainSingle();
    }
}
