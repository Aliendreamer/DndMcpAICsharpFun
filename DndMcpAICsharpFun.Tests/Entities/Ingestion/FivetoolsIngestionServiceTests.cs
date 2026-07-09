using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using DndMcpAICsharpFun.Features.VectorStore.Entities;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Ingestion;

public class FivetoolsIngestionServiceTests
{
    [Fact]
    public async Task Service_skips_entities_with_manual_data_source()
    {
        var store = Substitute.For<IEntityVectorStore>();
        var embeddings = Substitute.For<IEmbeddingService>();
        var dispatcher = new EntityCanonicalTextDispatcher();

        var manualId = "phb.class.fighter";
        store.GetDataSourcesAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string> { [manualId] = "manual" });
        embeddings.EmbedAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IList<float[]>>(
                Enumerable.Range(0, ci.Arg<IList<string>>().Count)
                    .Select(_ => new float[1024]).ToList()));

        var envelopes = new[]
        {
            new EntityEnvelope(manualId, EntityType.Class, "Fighter", "PHB", "Edition2014",
                null, new FirstAppearance("PHB", "Edition2014"), Array.Empty<Revision>(),
                Array.Empty<string>(), "", default, "5etools"),
            new EntityEnvelope("phb.class.wizard", EntityType.Class, "Wizard", "PHB", "Edition2014",
                null, new FirstAppearance("PHB", "Edition2014"), Array.Empty<Revision>(),
                Array.Empty<string>(), "", default, "5etools"),
        };

        var service = new FivetoolsIngestionService(store, embeddings, dispatcher,
            NullLogger<FivetoolsIngestionService>.Instance);

        await service.IngestEnvelopesAsync(envelopes, CancellationToken.None);

        await store.Received(1).UpsertAsync(
            Arg.Is<IList<EntityPoint>>(pts => pts.Count == 1 && pts[0].Envelope.Name == "Wizard"),
            Arg.Any<CancellationToken>());
    }
}