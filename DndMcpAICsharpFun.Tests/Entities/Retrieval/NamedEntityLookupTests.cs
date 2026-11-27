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

    [Fact]
    public void Printed_names_are_capitalised_runs_and_their_capitalised_parts()
    {
        NamedEntityLookup.PrintedNames("your Wisdom modifier Flurry of Blows Immediately after you attack")
            .Should().Contain(["flurry of blows", "flurry of blows immediately", "blows immediately"])
            .And.NotContain(["of blows", "flurry of"]);
        NamedEntityLookup.PrintedNames("TwInned SPEll When you cast").Should().Contain("twinned spell");
    }

    [Theory]
    [InlineData("What does Flurry of Blows cost a monk?")]
    [InlineData("what does flurry of blows cost a monk?")]
    [InlineData("WHAT DOES FLURRY OF BLOWS COST A MONK?")]
    [InlineData("what does flurry-of-blows cost")]
    public async Task The_question_finds_the_same_whatever_its_case_or_punctuation(string query)
    {
        var found = await Index(Corpus).FindAsync(query, null, CancellationToken.None);
        found.Select(f => f.Id).Should().Equal("phb14.class.monk");
    }

    [Fact]
    public async Task A_lowercase_question_finds_a_subclass_by_its_name()
    {
        var found = await Index(Corpus).FindAsync("what does dread ambusher add to a gloom stalker's initiative",
            null, CancellationToken.None);
        found[0].Id.Should().Be("xge.subclass.gloom-stalker");
        found[0].NameLength.Should().BeGreaterThan(1000);
    }

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
    public async Task A_phrase_in_many_entities_names_none()
    {
        var many = Enumerable.Range(0, 7).Select(i => Hit($"x{i}", $"X{i}", EntityType.Rule, "you take the Attack Action here")).ToArray();
        (await Index(many).FindAsync("the Attack Action", null, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task The_book_filter_holds_for_a_name_the_book_has_and_yields_for_one_it_lacks()
    {
        var corpus = Corpus.Append(Hit("mpmm.monster.juiblex", "Juiblex", EntityType.Monster, "Ooze lord.", "MPMM"))
            .Append(Hit("mtf.monster.juiblex", "Juiblex", EntityType.Monster, "Ooze lord.", "MTF")).ToArray();
        (await Index(corpus).FindAsync("Juiblex lair", "MTF", CancellationToken.None))
            .Select(f => f.Id).Should().Equal("mtf.monster.juiblex");
        // The model guessed the PHB; the subclass is in Xanathar's.
        (await Index(corpus).FindAsync("Gloom Stalker subclass", "PHB", CancellationToken.None))
            .Select(f => f.Id).Should().Equal("xge.subclass.gloom-stalker");
    }

    [Fact]
    public async Task A_capitalised_one_word_name_or_heading_names_its_entity()
    {
        var corpus = Corpus
            .Append(Hit("xge.subclass.samurai", "Samurai", EntityType.Subclass, "Samurai (Fighter subclass).", "XGE"))
            .Append(Hit("phb14.class.fighter", "Fighter", EntityType.Class,
                "Fighter — d10 hit die. Second Wind. You heal. Indomitable. Beginning at 9th level, you can reroll a saving throw."))
            .Append(Hit("phb14.rule.fire", "Fire", EntityType.Rule, "Fire burns."))
            .ToArray();
        var index = Index(corpus);

        (await index.FindAsync("Samurai subclass", "PHB", CancellationToken.None))
            .Select(f => f.Id).Should().Equal("xge.subclass.samurai");
        (await index.FindAsync("Indomitable", null, CancellationToken.None))
            .Select(f => f.Id).Should().Equal("phb14.class.fighter");
        // Lower case, or a one-word Rule title, names nothing.
        (await index.FindAsync("indomitable samurai", null, CancellationToken.None)).Should().BeEmpty();
        (await index.FindAsync("Fire damage", null, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public void Heading_words_are_one_word_paragraph_openers()
    {
        NamedEntityLookup.HeadingWords("Second Wind. You heal. Indomitable. Beginning at 9th level. Evasion (Rogue). When")
            .Should().Equal("indomitable", "evasion");
        NamedEntityLookup.CapitalisedWords("How often can I use Indomitable at 13th level?")
            .Should().Equal("indomitable");
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