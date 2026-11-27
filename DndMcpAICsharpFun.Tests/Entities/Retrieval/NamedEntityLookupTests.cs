using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.Retrieval.Entities;
using DndMcpAICsharpFun.Features.VectorStore.Entities;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Tests.Entities.Retrieval;

public sealed class NamedEntityLookupTests
{
    private static EntitySearchHit Hit(string id, string name, EntityType type, string text, string book = "PHB") =>
        new(new EntityEnvelope(id, type, name, book, "Edition2014",
            null, new FirstAppearance(book, "Edition2014"), [], [], text, default), 0f, id);

    private const string MonkText =
        "Monk — d8 hit die.\nKi save DC = 8 + your proficiency bonus + your Wisdom modifier Flurry of Blows " +
        "Immediately after you take the Attack action on your turn, you can spend 1 ki point to make two unarmed strikes.";

    private static readonly EntitySearchHit[] Corpus =
    [
        Hit("phb14.class.monk", "Monk", EntityType.Class, MonkText),
        Hit("xge.subclass.gloom-stalker", "Gloom Stalker", EntityType.Subclass,
            "Gloom Stalker (Ranger subclass).\nDread Ambusher. You can give yourself a bonus to your initiative rolls equal to your Wisdom modifier.", "XGE"),
        Hit("egw.monster.gloomstalker", "Gloomstalker", EntityType.Monster, "A shadowy bird.", "EGW"),
        Hit("mm14.monster.elder-tempest", "Elder Tempest", EntityType.Monster, "A storm. Flurry of wind."),
    ];

    private static IEntityVectorStore Store(params EntitySearchHit[] hits)
    {
        var store = Substitute.For<IEntityVectorStore>();
        store.ScrollAllAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<EntitySearchHit>>(hits));
        store.GetByIdsAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult<IReadOnlyDictionary<string, EntityEnvelope>>(
                hits.Where(h => ci.Arg<IReadOnlyList<string>>().Contains(h.Envelope.Id))
                    .ToDictionary(h => h.Envelope.Id, h => h.Envelope)));
        return store;
    }

    private static NamedEntityLookup Index(params EntitySearchHit[] hits) =>
        new(Store(hits), NullLogger<NamedEntityLookup>.Instance);

    [Theory]
    [InlineData("What does Flurry of Blows cost a monk?", "Flurry of Blows")]
    [InlineData("What does Dread Ambusher add to a Gloom Stalker's initiative?", "Dread Ambusher|Gloom Stalker")]
    [InlineData("Lore of the Raven Queen", "Lore of the Raven Queen")]
    [InlineData("what does a monk get", "")]
    [InlineData("Fireball damage", "")]                   // one capitalised word names nothing here
    public void Phrases_are_capitalised_runs_of_two_or_more_words(string query, string expected) =>
        NamedEntityLookup.Phrases(query).Should().BeEquivalentTo(expected.Split('|', StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public async Task A_feature_named_in_the_query_finds_the_entity_whose_text_holds_it()
    {
        var found = await Index(Corpus).FindAsync("Flurry of Blows", null, CancellationToken.None);
        found.Select(f => f.Id).Should().Equal("phb14.class.monk");
    }

    [Fact]
    public async Task The_entry_that_introduces_a_feature_comes_before_those_that_refer_back_to_it()
    {
        var found = await Index(
            Hit("xge.subclass.drunken-master", "Way of the Drunken Master", EntityType.Subclass,
                "When you use your Flurry of Blows, you gain more. Whenever you use Flurry of Blows, you move."),
            Corpus[0]).FindAsync("Flurry of Blows", null, CancellationToken.None);
        found.Select(f => f.Id).Should().Equal("phb14.class.monk", "xge.subclass.drunken-master");
    }

    [Fact]
    public async Task An_entity_name_match_comes_before_a_feature_match()
    {
        var found = await Index(Corpus).FindAsync(
            "What does Dread Ambusher add to a Gloom Stalker's initiative?", null, CancellationToken.None);
        found[0].Id.Should().Be("xge.subclass.gloom-stalker");
        found[0].NameLength.Should().BeGreaterThan(1000);
        found.Select(f => f.Id).Should().NotContain("egw.monster.gloomstalker");
    }

    [Fact]
    public async Task A_phrase_in_many_entities_names_none_and_the_book_filter_holds()
    {
        var many = Enumerable.Range(0, 7).Select(i => Hit($"x{i}", $"X{i}", EntityType.Rule, "you take the Attack Action here")).ToArray();
        (await Index(many).FindAsync("the Attack Action", null, CancellationToken.None)).Should().BeEmpty();
        (await Index(Corpus).FindAsync("Gloom Stalker", "PHB", CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_named_feature_the_vectors_missed_joins_the_results_first()
    {
        var store = Store(Corpus);
        // The vectors bring only the monster.
        store.SearchAsync(Arg.Any<float[]>(), Arg.Any<EntityFilters>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<EntitySearchHit>>([Corpus[3]]));
        var emb = Substitute.For<IEmbeddingService>();
        emb.EmbedAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<float[]>>([new[] { 0.1f, 0.2f }]));
        var rerankOpts = new RerankerOptions { Enabled = false };
        var sut = new EntityRetrievalService(emb, store, Options.Create(new RetrievalOptions()),
            new RerankingService(Substitute.For<IReranker>(), Options.Create(rerankOpts)), Options.Create(rerankOpts),
            new SpellClassIndex("__no_5etools_dir__"), new NamedEntityLookup(store, NullLogger<NamedEntityLookup>.Instance));

        var results = await sut.SearchAsync(
            new EntitySearchQuery("Flurry of Blows", null, null, null, null, null, null, null, null, null, null, TopK: 5),
            CancellationToken.None);

        results.Select(r => r.Id).Should().Equal("phb14.class.monk", "mm14.monster.elder-tempest");
        results[0].Snippet.Should().Contain("two unarmed strikes");
    }
}