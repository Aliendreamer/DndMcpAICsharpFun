using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.Retrieval.Entities;
using DndMcpAICsharpFun.Features.VectorStore.Entities;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Entities.Retrieval;

public sealed class EntityRetrievalServiceRerankerTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static EntityEnvelope MakeEnvelope(string id, string canonicalText) =>
        new(id, EntityType.Spell, id, "PHB", "Edition2014",
            null, new FirstAppearance("PHB", "Edition2014"), [],
            [], canonicalText, default);

    private static EntitySearchHit MakeHit(string id, string canonicalText, float score) =>
        new(MakeEnvelope(id, canonicalText), score, id + "-point");

    private static IEmbeddingService MakeEmbedding()
    {
        var emb = Substitute.For<IEmbeddingService>();
        emb.EmbedAsync(Arg.Any<IList<string>>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<IList<float[]>>([new float[] { 0.1f, 0.2f }]));
        return emb;
    }

    private static EntityRetrievalService BuildSut(
        IEntityVectorStore store,
        IReranker reranker,
        bool globalEnabled = true,
        bool rerankEntities = true,
        int candidatePoolSize = 20,
        int maxTopK = 50,
        SpellClassIndex? spellClassIndex = null,
        EntityTextMode entityText = EntityTextMode.Pieces)
    {
        var rerankOpts = new RerankerOptions
        {
            Enabled = globalEnabled,
            RerankEntities = rerankEntities,
            CandidatePoolSize = candidatePoolSize,
        };
        var rerankSvc = new RerankingService(reranker, Options.Create(rerankOpts));
        return new EntityRetrievalService(
            MakeEmbedding(),
            store,
            Options.Create(new RetrievalOptions { MaxTopK = maxTopK, EntityText = entityText }),
            rerankSvc,
            Options.Create(rerankOpts),
            spellClassIndex ?? new SpellClassIndex("__no_5etools_dir__"));
    }

    // ── Task 3.1 / Spec: "Entity search returns reranked results" ────────────

    [Fact]
    public async Task SearchAsync_WhenRerankEntitiesEnabled_OverFetchesPoolAndReranks()
    {
        // Arrange: store returns 5 hits; reranker scores reverse the order
        var store = Substitute.For<IEntityVectorStore>();
        var hits = new[]
        {
            MakeHit("e-low",  "lowest score text",  0.1f),
            MakeHit("e-high", "highest score text", 0.9f),
            MakeHit("e-mid",  "mid score text",     0.5f),
        };
        store.SearchAsync(Arg.Any<float[]>(), Arg.Any<EntityFilters>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult<IList<EntitySearchHit>>(hits));

        var mockReranker = Substitute.For<IReranker>();
        mockReranker.Enabled.Returns(true);
        // Score: e-low=0.95, e-high=0.1, e-mid=0.5 → reranked: e-low, e-mid, e-high
        mockReranker
            .RerankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new float[] { 0.95f, 0.1f, 0.5f }));

        var sut = BuildSut(store, mockReranker, globalEnabled: true, rerankEntities: true, candidatePoolSize: 10);
        var query = new EntitySearchQuery("fireball", null, null, null, null, null, null,
            null, null, null, null, TopK: 2);

        // Act
        var results = await sut.SearchAsync(query, CancellationToken.None);

        // Assert: over-fetched pool (10), reranked, top 2 returned
        await store.Received(1).SearchAsync(
            Arg.Any<float[]>(), Arg.Any<EntityFilters>(), 10, Arg.Any<CancellationToken>());

        results.Should().HaveCount(2);
        results[0].Id.Should().Be("e-low");   // highest rerank score
        results[1].Id.Should().Be("e-mid");   // second
    }

    // ── chat-grounded-answers: named results first, wide text ────────────────

    private static EntitySearchHit Named(string name, string text, EntityType type = EntityType.Monster) =>
        new(MakeEnvelope(name, text) with { Type = type }, 0.5f, name + "-point");

    private static Task<IList<EntitySearchResult>> SearchInOrder(
        string query, int topK, bool rerank, params EntitySearchHit[] hits) =>
        SearchInOrder(EntityTextMode.Pieces, query, topK, rerank, hits);

    private static async Task<IList<EntitySearchResult>> SearchInOrder(
        EntityTextMode entityText, string query, int topK, bool rerank, params EntitySearchHit[] hits)
    {
        var store = Substitute.For<IEntityVectorStore>();
        store.SearchAsync(Arg.Any<float[]>(), Arg.Any<EntityFilters>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult<IList<EntitySearchHit>>(hits));
        var reranker = Substitute.For<IReranker>();
        reranker.Enabled.Returns(true);
        // Keeps the store's order, except that a piece holding "NEEDLE" beats its own entity's other pieces.
        reranker.RerankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(ci.Arg<IReadOnlyList<string>>()
                .Select((p, i) => -i + (p.Contains("NEEDLE", StringComparison.Ordinal) ? 100f : 0f)).ToArray()));
        var sut = BuildSut(store, reranker, rerankEntities: rerank, entityText: entityText);
        return await sut.SearchAsync(
            new EntitySearchQuery(query, null, null, null, null, null, null, null, null, null, null, TopK: topK),
            CancellationToken.None);
    }

    [Theory]
    [InlineData("What are the armor class and hit points of the tarrasque?")]
    [InlineData("TARRASQUE")]
    [InlineData("how do two tarrasques fight")]
    public async Task A_result_named_in_the_query_comes_first(string query)
    {
        var results = await SearchInOrder(query, 2, true,
            Named("Statistics for Objects", "a"), Named("Ram", "b"), Named("Tarrasque", "c"));

        results.Select(r => r.Name).Should().Equal("Tarrasque", "Statistics for Objects");
    }

    [Fact]
    public async Task The_longest_name_comes_first_and_part_of_a_word_is_no_name()
    {
        var results = await SearchInOrder("giant octopus armor class", 4, true,
            Named("Ant", "a"), Named("Octopus", "b"), Named("Giant Octopus", "c"));

        results.Select(r => r.Name).Should().Equal("Giant Octopus", "Octopus", "Ant");
    }

    [Fact]
    public async Task A_rule_named_in_passing_is_not_put_first()
    {
        var hits = new[] { Named("Grappling", "a", EntityType.Rule), Named("Attack", "b", EntityType.Rule) };

        (await SearchInOrder("does a grapple replace an attack", 2, true, hits))[0].Name.Should().Be("Grappling");
        (await SearchInOrder("attack", 2, true, hits))[0].Name.Should().Be("Attack");
    }

    [Fact]
    public async Task The_first_three_results_carry_wide_text_and_the_rest_a_snippet()
    {
        var text = string.Concat(Enumerable.Repeat("Some words of the book. ", 300));
        var results = await SearchInOrder("what does Wish do", 5, true,
            Named("B", text), Named("Wish", text), Named("C", text), Named("D", text));

        results.Select(r => r.Name).Should().Equal("Wish", "B", "C", "D");
        results[0].Snippet.Length.Should().BeInRange(3000, 3002);
        results[1].Snippet.Length.Should().BeInRange(1400, 1502);
        results[2].Snippet.Length.Should().BeInRange(1400, 1502);
        results[3].Snippet.Length.Should().Be(241);
    }


    [Fact]
    public async Task Whole_mode_gives_the_named_result_its_whole_text_when_it_fits()
    {
        var fits = string.Concat(Enumerable.Repeat("Some words of the book. ", 200));   // 4800 chars
        var tooLong = string.Concat(Enumerable.Repeat("Some words of the book. ", 700)); // 16800 chars, past the 14000 cap
        var results = await SearchInOrder(EntityTextMode.Whole, "what does Wish do", 3, true,
            Named("Wish", fits), Named("B", fits));
        results[0].Snippet.Should().Be(fits);
        results[1].Snippet.Length.Should().BeLessThan(1600);   // not named: pieces, as before

        var cut = await SearchInOrder(EntityTextMode.Whole, "what does Wish do", 3, true, Named("Wish", tooLong));
        cut[0].Snippet.Length.Should().BeInRange(3000, 3002);
    }

    [Fact]
    public async Task ModelPicks_lists_the_parts_of_a_long_text_and_only_there()
    {
        var text = "Wish\n" + string.Concat(Enumerable.Repeat("Some words of the book. ", 150))
            + "\nTwInned SPEll\n" + string.Concat(Enumerable.Repeat("Some words of the book. ", 150));
        var results = await SearchInOrder(EntityTextMode.ModelPicks, "what does Wish do", 3, true,
            Named("Wish", text), Named("B", "short"));
        results[0].Parts.Should().NotBeNull();
        results[0].Parts!.Should().HaveCount(EntityTextPieces.Split(text).Count);
        results[0].Parts!.Should().Contain(p => p.Contains("Twinned Spell"));
        results[1].Parts.Should().BeNull();

        var pieces = await SearchInOrder("what does Wish do", 3, true, Named("Wish", text));
        pieces[0].Parts.Should().BeNull();
    }

    [Fact]
    public async Task The_top_result_carries_the_widest_text_even_when_unnamed()
    {
        // Travel Pace: 2,178 characters with its table (30 miles a day) at the end; a 1,500 window lost the table.
        var text = string.Concat(Enumerable.Repeat("Some words of the book. ", 90)) + "Fast 30 miles";

        var results = await SearchInOrder("how far does a party travel at a fast pace", 2, true,
            Named("Travel Pace", text), Named("B", text));

        results[0].Snippet.Should().Be(text);
        results[1].Snippet.Length.Should().BeInRange(1400, 1502);
    }


    [Fact]
    public async Task A_named_feature_puts_its_monster_first_with_the_feature_in_view()
    {
        var filler = string.Concat(Enumerable.Repeat("The devourer lurks in skulls. ", 70));
        var devourer = filler + "\nDevour Intellect. The target must succeed on a DC 12 Intelligence saving throw; roll 3d6.\n" + filler;

        var results = await SearchInOrder("After a failed save against Devour Intellect, which dice are rolled?", 3, true,
            Named("Death Saving Throws", "Roll a d20.", EntityType.Rule), Named("Lucky", "Reroll a d20.", EntityType.Feat),
            Named("Intellect Devourer", devourer));

        results[0].Name.Should().Be("Intellect Devourer");
        results[0].Snippet.Should().Contain("roll 3d6");
    }


    private static async Task<IList<EntitySearchResult>> SearchTyped(
        string query, EntityType type, EntitySearchHit[] ofType, EntitySearchHit[] anyType)
    {
        var store = Substitute.For<IEntityVectorStore>();
        store.SearchAsync(Arg.Any<float[]>(), Arg.Is<EntityFilters>(f => f.Type == type), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult<IList<EntitySearchHit>>(ofType));
        store.SearchAsync(Arg.Any<float[]>(), Arg.Is<EntityFilters>(f => f.Type == null), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult<IList<EntitySearchHit>>(anyType));
        var reranker = Substitute.For<IReranker>();
        reranker.Enabled.Returns(true);
        reranker.RerankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(ci.Arg<IReadOnlyList<string>>().Select((_, i) => -(float)i).ToArray()));
        var sut = BuildSut(store, reranker, rerankEntities: true);
        return await sut.SearchAsync(
            new EntitySearchQuery(query, type, null, null, null, null, null, null, null, null, null, TopK: 3),
            CancellationToken.None);
    }

    [Fact]
    public async Task A_wrong_type_guess_does_not_hide_the_entity_the_query_names()
    {
        var results = await SearchTyped("Devour Intellect", EntityType.Spell,
            ofType: [Named("Feeblemind", "Enchantment.", EntityType.Spell), Named("Psychic Scream", "Enchantment.", EntityType.Spell)],
            anyType: [Named("Intellect Devourer", "Devour Intellect. Roll 3d6."), Named("Owlbear", "Beak. Hit.")]);

        results.Select(r => r.Name).Should().Equal("Intellect Devourer", "Feeblemind", "Psychic Scream");
    }

    [Fact]
    public async Task An_unnamed_result_of_another_type_stays_out()
    {
        var results = await SearchTyped("a spell that blasts minds", EntityType.Spell,
            ofType: [Named("Psychic Scream", "Enchantment.", EntityType.Spell)],
            anyType: [Named("Owlbear", "Beak. Hit.")]);

        results.Select(r => r.Name).Should().Equal("Psychic Scream");
    }


    // The stored text of a stat block lists traits and actions as indented "Name: text" lines.
    private const string StoredDevourer =
        "Intellect Devourer — Tiny aberration\nAC 12\nSaving Throws: int +2\nActions:\n" +
        "  Multiattack: The intellect devourer makes one attack with its claws and uses Devour Intellect.\n" +
        "  Devour Intellect: The target must succeed on a DC 12 Intelligence saving throw; roll 3d6.\n";

    [Fact]
    public async Task A_named_feature_in_the_stored_stat_block_format_puts_its_monster_first()
    {
        var results = await SearchInOrder("After a failed save against Devour Intellect, which dice are rolled?", 3, true,
            Named("Intellect Devourer Lore", "A walking brain.", EntityType.Lore), Named("Lucky", "Reroll a d20.", EntityType.Feat),
            Named("Intellect Devourer", StoredDevourer));

        results[0].Name.Should().Be("Intellect Devourer");
    }

    [Fact]
    public async Task A_stat_line_is_not_a_feature()
    {
        var results = await SearchInOrder("which saving throws does it have", 2, true,
            Named("Owlbear", "Beak. Hit."), Named("Intellect Devourer", StoredDevourer));

        results.Select(r => r.Name).Should().Equal("Owlbear", "Intellect Devourer");
    }

    [Theory]
    [InlineData("how do claws work")]
    [InlineData("tell me about the devour")]
    public async Task A_one_word_feature_or_part_of_one_names_nothing(string query)
    {
        var results = await SearchInOrder(query, 2, true,
            Named("A", "Some text."), Named("Intellect Devourer", "Claws. Melee.\nDevour Intellect. Roll 3d6."));

        results[0].Name.Should().Be("A");
    }

    [Fact]
    public async Task Long_text_is_cut_around_the_best_piece()
    {
        var filler = string.Concat(Enumerable.Repeat("Some words of the book. ", 250));

        var results = await SearchInOrder("what does Wish do", 1, true, Named("Wish", filler + "The wish NEEDLE. " + filler));

        results[0].Snippet.Should().Contain("NEEDLE");
        results[0].Snippet.Length.Should().BeLessThanOrEqualTo(3002);
    }

    [Fact]
    public async Task Without_a_reranker_named_results_still_come_first_with_wide_text()
    {
        var text = string.Concat(Enumerable.Repeat("Some words of the book. ", 20));

        var results = await SearchInOrder("wish", 2, false, Named("B", text), Named("Wish", text));

        results[0].Name.Should().Be("Wish");
        results[0].Snippet.Should().Be(text);
    }

    // ── Task 3.1 / Spec: "Entity reranking can be disabled" ──────────────────

    [Fact]
    public async Task SearchAsync_WhenRerankEntitiesFalse_FetchesTopKAndSkipsRerank()
    {
        var store = Substitute.For<IEntityVectorStore>();
        var hits = new[]
        {
            MakeHit("vec-first",  "alpha", 0.9f),
            MakeHit("vec-second", "beta",  0.7f),
            MakeHit("vec-third",  "gamma", 0.5f),
        };
        store.SearchAsync(Arg.Any<float[]>(), Arg.Any<EntityFilters>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult<IList<EntitySearchHit>>(hits));

        var mockReranker = Substitute.For<IReranker>();
        mockReranker.Enabled.Returns(true);

        var sut = BuildSut(store, mockReranker, globalEnabled: true, rerankEntities: false, candidatePoolSize: 20);
        var query = new EntitySearchQuery("fireball", null, null, null, null, null, null,
            null, null, null, null, TopK: 2);

        var results = await sut.SearchAsync(query, CancellationToken.None);

        // fetched exactly topK=2, not pool size
        await store.Received(1).SearchAsync(
            Arg.Any<float[]>(), Arg.Any<EntityFilters>(), 2, Arg.Any<CancellationToken>());

        // reranker NOT called
        await mockReranker.DidNotReceive()
            .RerankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());

        // vector order preserved
        results.Should().HaveCount(2);
        results[0].Id.Should().Be("vec-first");
        results[1].Id.Should().Be("vec-second");
    }

    // ── Task 2.1 / Spec: "Global kill-switch overrides channel flags" ─────────

    [Fact]
    public async Task SearchAsync_WhenGlobalEnabledFalse_SkipsRerankEvenIfRerankEntitiesTrue()
    {
        var store = Substitute.For<IEntityVectorStore>();
        var hits = new[]
        {
            MakeHit("a", "text-a", 0.9f),
            MakeHit("b", "text-b", 0.7f),
        };
        store.SearchAsync(Arg.Any<float[]>(), Arg.Any<EntityFilters>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult<IList<EntitySearchHit>>(hits));

        var mockReranker = Substitute.For<IReranker>();
        mockReranker.Enabled.Returns(true);

        var sut = BuildSut(store, mockReranker, globalEnabled: false, rerankEntities: true, candidatePoolSize: 20);
        var query = new EntitySearchQuery("test", null, null, null, null, null, null,
            null, null, null, null, TopK: 2);

        await sut.SearchAsync(query, CancellationToken.None);

        await mockReranker.DidNotReceive()
            .RerankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }

    // ── Task 2.1 / Spec: "Candidate pool size is honored" ────────────────────

    [Fact]
    public async Task SearchAsync_WhenRerankEnabled_FetchesCandidatePoolSizeNotTopK()
    {
        var store = Substitute.For<IEntityVectorStore>();
        store.SearchAsync(Arg.Any<float[]>(), Arg.Any<EntityFilters>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult<IList<EntitySearchHit>>([]));

        var mockReranker = Substitute.For<IReranker>();
        mockReranker.Enabled.Returns(true);

        // CandidatePoolSize=30 but TopK=5 — store should be called with 30
        var sut = BuildSut(store, mockReranker, globalEnabled: true, rerankEntities: true, candidatePoolSize: 30);
        var query = new EntitySearchQuery("test", null, null, null, null, null, null,
            null, null, null, null, TopK: 5);

        await sut.SearchAsync(query, CancellationToken.None);

        await store.Received(1).SearchAsync(
            Arg.Any<float[]>(), Arg.Any<EntityFilters>(), 30, Arg.Any<CancellationToken>());
    }

    // ── extraction-authority-ladder T3 (3.5): the authority label is surfaced on results ──

    [Fact]
    public async Task SearchAsync_SurfacesTheAuthorityLabelFromTheEnvelope()
    {
        var store = Substitute.For<IEntityVectorStore>();
        var hit = new EntitySearchHit(
            MakeEnvelope("e1", "flumph text") with { Authority = "verified-thirdparty" }, 0.9f, "e1-point");
        store.SearchAsync(Arg.Any<float[]>(), Arg.Any<EntityFilters>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult<IList<EntitySearchHit>>([hit]));

        // Reranking off → the plain vector path preserves the envelope, so we assert on surfacing.
        var sut = BuildSut(store, Substitute.For<IReranker>(), globalEnabled: false);
        var query = new EntitySearchQuery("flumph", null, null, null, null, null, null,
            null, null, null, null, TopK: 10);

        var results = await sut.SearchAsync(query, CancellationToken.None);

        results.Should().ContainSingle();
        results[0].Authority.Should().Be("verified-thirdparty");
    }

    // ── entity-set-query: complete filter-set (ListAsync) ────────────────────

    [Fact]
    public async Task ListAsync_returns_complete_set_with_honest_total_and_compact_rows()
    {
        var store = Substitute.For<IEntityVectorStore>();
        IReadOnlyList<EntitySearchHit> hits = [MakeHit("mm.monster.a", "txt", 0f), MakeHit("mm.monster.b", "txt", 0f)];
        store.ListByFilterAsync(Arg.Any<EntityFilters>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult((137, hits)));

        var sut = BuildSut(store, Substitute.For<IReranker>());
        var q = new EntitySearchQuery("", EntityType.Monster, null, null, null, null, null,
            null, null, null, null, TopK: 2);

        var result = await sut.ListAsync(q, cap: 2, CancellationToken.None);

        result.Total.Should().Be(137);          // the TRUE match count
        result.Returned.Should().Be(2);         // capped rows
        (result.Total > result.Returned).Should().BeTrue(); // truncation signal
        result.Rows.Should().HaveCount(2);
        result.Rows[0].Id.Should().Be("mm.monster.a");
    }

    // ── spell-class-join: castableByClass ────────────────────────────────────

    private static EntitySearchHit MakeSpellHit(string id, string name, string source) =>
        new(new EntityEnvelope(id, EntityType.Spell, name, source, "Edition2014", null,
            new FirstAppearance(source, "Edition2014"), [], [], "", default), 0f, id + "-pt");

    private static string WriteSpellSources()
    {
        var dir = Directory.CreateTempSubdirectory("scix");
        Directory.CreateDirectory(Path.Combine(dir.FullName, "spells"));
        File.WriteAllText(Path.Combine(dir.FullName, "spells", "sources.json"), """
        { "PHB": {
            "Fireball":    { "class": [ { "name": "Wizard" } ] },
            "Mage Armor":  { "class": [ { "name": "Wizard" } ] },
            "Cure Wounds": { "class": [ { "name": "Cleric" } ] }
        } }
        """);
        return dir.FullName;
    }

    [Fact]
    public async Task ListAsync_castableByClass_returns_only_that_class_spells_with_class_filtered_total()
    {
        var store = Substitute.For<IEntityVectorStore>();
        IReadOnlyList<EntitySearchHit> spells =
        [
            MakeSpellHit("phb.spell.fireball", "Fireball", "PHB"),
            MakeSpellHit("phb.spell.cure-wounds", "Cure Wounds", "PHB"),
            MakeSpellHit("phb.spell.mage-armor", "Mage Armor", "PHB"),
        ];
        store.ListByFilterAsync(Arg.Any<EntityFilters>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult((3, spells)));

        var dir = WriteSpellSources();
        try
        {
            var sut = BuildSut(store, Substitute.For<IReranker>(), spellClassIndex: new SpellClassIndex(dir));
            var q = new EntitySearchQuery("", EntityType.Spell, null, null, null, null, null,
                null, null, null, null, TopK: 50, CastableByClass: "Wizard");

            var result = await sut.ListAsync(q, 50, CancellationToken.None);

            result.Total.Should().Be(2); // class-filtered count, NOT the 3 scrolled spells
            result.Rows.Select(r => r.Name).Should().BeEquivalentTo("Fireball", "Mage Armor");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }


    // ── race-ability-filter: abilityBonus ─────────────────────────────────────

    private static EntitySearchHit MakeRaceHit(string id, string name, string abilityJson) =>
        new(new EntityEnvelope(id, EntityType.Race, name, "PHB", "Edition2014", null,
            new FirstAppearance("PHB", "Edition2014"), [], [], "", JsonDocument.Parse(abilityJson).RootElement),
            0f, id + "-pt");

    private static IEntityVectorStore MakeRaceStoreForAbilityBonus()
    {
        var store = Substitute.For<IEntityVectorStore>();
        IReadOnlyList<EntitySearchHit> races =
        [
            MakeRaceHit("phb.race.mountain-dwarf", "Mountain Dwarf", """{"ability":[{"str":2}]}"""),
            MakeRaceHit("phb.race.half-elf", "Half-Elf", """{"ability":[{"choose":{"from":["str","dex"]}}]}"""),
            MakeRaceHit("phb.race.wood-elf", "Wood Elf", """{"ability":[{"dex":2}]}"""),
        ];
        store.ListByFilterAsync(Arg.Any<EntityFilters>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult((3, races)));
        return store;
    }

    [Fact]
    public async Task ListAsync_abilityBonus_returns_only_races_that_boost_that_ability()
    {
        var store = MakeRaceStoreForAbilityBonus();
        var sut = BuildSut(store, Substitute.For<IReranker>());
        var q = new EntitySearchQuery("", EntityType.Race, null, null, null, null, null,
            null, null, null, null, TopK: 50, AbilityBonus: "str");

        var result = await sut.ListAsync(q, 50, CancellationToken.None);

        result.Total.Should().Be(2); // dex-only race excluded from the class-filtered total
        result.Rows.Select(r => r.Name).Should().BeEquivalentTo("Mountain Dwarf", "Half-Elf");
    }

    [Fact]
    public async Task ListAsync_abilityBonus_is_case_insensitive()
    {
        var store = MakeRaceStoreForAbilityBonus();
        var sut = BuildSut(store, Substitute.For<IReranker>());
        var q = new EntitySearchQuery("", EntityType.Race, null, null, null, null, null,
            null, null, null, null, TopK: 50, AbilityBonus: "STR");

        var result = await sut.ListAsync(q, 50, CancellationToken.None);

        result.Total.Should().Be(2);
        result.Rows.Select(r => r.Name).Should().BeEquivalentTo("Mountain Dwarf", "Half-Elf");
    }

    [Fact]
    public async Task ListAsync_abilityBonus_null_uses_the_generic_path_unchanged()
    {
        var store = Substitute.For<IEntityVectorStore>();
        IReadOnlyList<EntitySearchHit> hits = [MakeHit("mm.monster.a", "txt", 0f)];
        store.ListByFilterAsync(Arg.Any<EntityFilters>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult((1, hits)));

        var sut = BuildSut(store, Substitute.For<IReranker>());
        var q = new EntitySearchQuery("", EntityType.Monster, null, null, null, null, null,
            null, null, null, null, TopK: 50, AbilityBonus: null);

        var result = await sut.ListAsync(q, 50, CancellationToken.None);

        result.Total.Should().Be(1);
        result.Rows.Should().ContainSingle(r => r.Id == "mm.monster.a");
    }

    // ── race-damage-defense-filter: resist/immune ──────────────────────────────

    private static IEntityVectorStore MakeRaceStoreForDamageDefense()
    {
        var store = Substitute.For<IEntityVectorStore>();
        IReadOnlyList<EntitySearchHit> races =
        [
            MakeRaceHit("phb.race.fire-genasi", "Fire Genasi", """{"resist":["fire"]}"""),
            MakeRaceHit("phb.race.tiefling", "Tiefling", """{"resist":[{"choose":{"from":["fire","cold"]}}]}"""),
            MakeRaceHit("phb.race.hill-dwarf", "Hill Dwarf", """{"immune":["poison"]}"""),
            MakeRaceHit("phb.race.human", "Human", """{}"""),
        ];
        store.ListByFilterAsync(Arg.Any<EntityFilters>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult((4, races)));
        return store;
    }

    [Fact]
    public async Task ListAsync_resistDamage_returns_only_races_that_resist_that_damage_type()
    {
        var store = MakeRaceStoreForDamageDefense();
        var sut = BuildSut(store, Substitute.For<IReranker>());
        var q = new EntitySearchQuery("", EntityType.Race, null, null, null, null, null,
            null, null, null, null, TopK: 50, ResistDamage: "fire");

        var result = await sut.ListAsync(q, 50, CancellationToken.None);

        result.Total.Should().Be(2);
        result.Rows.Select(r => r.Name).Should().BeEquivalentTo("Fire Genasi", "Tiefling");
    }

    [Fact]
    public async Task ListAsync_immuneDamage_returns_only_races_that_are_immune_to_that_damage_type()
    {
        var store = MakeRaceStoreForDamageDefense();
        var sut = BuildSut(store, Substitute.For<IReranker>());
        var q = new EntitySearchQuery("", EntityType.Race, null, null, null, null, null,
            null, null, null, null, TopK: 50, ImmuneDamage: "poison");

        var result = await sut.ListAsync(q, 50, CancellationToken.None);

        result.Total.Should().Be(1);
        result.Rows.Should().ContainSingle(r => r.Name == "Hill Dwarf");
    }

    [Fact]
    public async Task ListAsync_resistDamage_and_immuneDamage_together_ANDs_both_and_can_yield_none()
    {
        var store = MakeRaceStoreForDamageDefense();
        var sut = BuildSut(store, Substitute.For<IReranker>());
        var q = new EntitySearchQuery("", EntityType.Race, null, null, null, null, null,
            null, null, null, null, TopK: 50, ResistDamage: "fire", ImmuneDamage: "poison");

        var result = await sut.ListAsync(q, 50, CancellationToken.None);

        result.Total.Should().Be(0);
        result.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAsync_resistDamage_is_case_insensitive()
    {
        var store = MakeRaceStoreForDamageDefense();
        var sut = BuildSut(store, Substitute.For<IReranker>());
        var q = new EntitySearchQuery("", EntityType.Race, null, null, null, null, null,
            null, null, null, null, TopK: 50, ResistDamage: "FIRE");

        var result = await sut.ListAsync(q, 50, CancellationToken.None);

        result.Total.Should().Be(2);
        result.Rows.Select(r => r.Name).Should().BeEquivalentTo("Fire Genasi", "Tiefling");
    }

    [Fact]
    public async Task ListAsync_resistDamage_and_immuneDamage_null_uses_the_generic_path_unchanged()
    {
        var store = Substitute.For<IEntityVectorStore>();
        IReadOnlyList<EntitySearchHit> hits = [MakeHit("mm.monster.a", "txt", 0f)];
        store.ListByFilterAsync(Arg.Any<EntityFilters>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(Task.FromResult((1, hits)));

        var sut = BuildSut(store, Substitute.For<IReranker>());
        var q = new EntitySearchQuery("", EntityType.Monster, null, null, null, null, null,
            null, null, null, null, TopK: 50, ResistDamage: null, ImmuneDamage: null);

        var result = await sut.ListAsync(q, 50, CancellationToken.None);

        result.Total.Should().Be(1);
        result.Rows.Should().ContainSingle(r => r.Id == "mm.monster.a");
    }
}