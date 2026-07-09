using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Retrieval.Entities.Dedup;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using DndMcpAICsharpFun.Tests.TestDoubles;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Retrieval.Entities.Dedup;

public sealed class EntityDuplicateServiceTests
{
    private sealed class FakeBookTypeLookup : IBookTypeLookup
    {
        public Task<IReadOnlyDictionary<string, BookType>> BuildAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<string, BookType>>(new Dictionary<string, BookType>
            {
                ["PHB"] = BookType.Core,
                ["XGE"] = BookType.Supplement,
            });
    }

    private static async Task<(RecordingEntityVectorStore Store, EntityDuplicateService Service, string PhbId, string XgeId)> SeedAsync()
    {
        var store = new RecordingEntityVectorStore();
        var phb = TestEnvelopes.Make("phb.spell.fireball", "Fireball", EntityType.Spell, "Edition2014", sourceBook: "PHB");
        var xge = TestEnvelopes.Make("xge.spell.fireball", "Fireball", EntityType.Spell, "Edition2014", sourceBook: "XGE");
        var iceKnife = TestEnvelopes.Make("xge.spell.ice-knife", "Ice Knife", EntityType.Spell, "Edition2014", sourceBook: "XGE");

        await store.UpsertAsync(
        [
            new EntityPoint(phb, [0f], "hash-phb"),
            new EntityPoint(xge, [0f], "hash-xge"),
            new EntityPoint(iceKnife, [0f], "hash-ice"),
        ]);

        var service = new EntityDuplicateService(store, new FakeBookTypeLookup());
        return (store, service, phb.Id, xge.Id);
    }

    [Fact]
    public async Task FindDuplicatesAsync_groups_the_fireball_pair_and_ignores_the_unique_entity()
    {
        var (_, service, phbId, xgeId) = await SeedAsync();

        var report = await service.FindDuplicatesAsync();

        report.GroupCount.Should().Be(1);
        report.LoserCount.Should().Be(1);
        var group = report.Groups.Should().ContainSingle().Subject;
        group.WinnerId.Should().Be(phbId);
        group.LoserIds.Should().BeEquivalentTo([xgeId]);
    }

    [Fact]
    public async Task CompactAsync_with_apply_false_reports_but_deletes_nothing()
    {
        var (store, service, phbId, xgeId) = await SeedAsync();

        var report = await service.CompactAsync(apply: false);

        report.GroupCount.Should().Be(1);
        report.Groups.Should().ContainSingle().Which.WinnerId.Should().Be(phbId);
        store.Ids.Should().Contain([phbId, xgeId]);
    }

    [Fact]
    public async Task CompactAsync_with_apply_true_deletes_only_the_loser()
    {
        var (store, service, phbId, xgeId) = await SeedAsync();

        await service.CompactAsync(apply: true);

        store.Ids.Should().Contain(phbId);
        store.Ids.Should().NotContain(xgeId);
    }
}