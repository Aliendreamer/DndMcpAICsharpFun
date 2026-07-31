using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Campaigns;
using DndMcpAICsharpFun.Tests.Persistence;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Persistence;

[Collection("postgres")]
public sealed class CampaignEditionAndBooksTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly CampaignRepository _repo = new(new TestDb(pg));
    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task New_2014_campaign_defaults_to_the_2014_core_only()
    {
        var id = await _repo.CreateAsync(1, "Classic", "d");
        var c = (await _repo.GetByIdAsync(id, 1))!;

        c.Edition.Should().Be(DndVersion.Edition2014);
        c.AllowedBookKeys.Should().BeEquivalentTo(["PHB", "DMG", "MM"],
            "supplements are opt-in for a new table");
    }

    [Fact]
    public async Task New_2024_campaign_defaults_to_the_revised_core_only()
    {
        var id = await _repo.CreateAsync(1, "Revised", "d", edition: DndVersion.Edition2024);
        var c = (await _repo.GetByIdAsync(id, 1))!;

        c.Edition.Should().Be(DndVersion.Edition2024);
        c.AllowedBookKeys.Should().BeEquivalentTo(["XPHB", "XDMG", "XMM"]);
    }

    // The motivating case: 2024 rules, 2014-era setting. Must persist exactly as given.
    [Fact]
    public async Task An_explicit_mixed_canon_allowlist_round_trips_verbatim()
    {
        var id = await _repo.CreateAsync(
            1, "Eberron 2024", "d",
            edition: DndVersion.Edition2024,
            allowedBookKeys: ["XPHB", "XDMG", "ERLW", "XGE"]);

        var c = (await _repo.GetByIdAsync(id, 1))!;
        c.AllowedBookKeys.Should().BeEquivalentTo(["XPHB", "XDMG", "ERLW", "XGE"],
            "an explicit list must not be overwritten by the core-only default");

        var scope = CampaignScopeResolver.Resolve(c);
        scope.MechanicsScope.Should().BeEquivalentTo(["XPHB", "XDMG"]);
        scope.LoreScope.Should().Contain("ERLW", "a 2024 Eberron table keeps its setting");
    }

    [Fact]
    public async Task An_explicitly_empty_allowlist_is_preserved_not_replaced_by_the_default()
    {
        var id = await _repo.CreateAsync(1, "Empty", "d", allowedBookKeys: []);
        (await _repo.GetByIdAsync(id, 1))!.AllowedBookKeys.Should().BeEmpty();
    }

    [Fact]
    public async Task An_unknown_key_is_rejected_on_create()
    {
        var act = () => _repo.CreateAsync(1, "Bad", "d", allowedBookKeys: ["PHB", "NOT_A_BOOK"]);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*NOT_A_BOOK*");
    }

    [Fact]
    public async Task An_unknown_key_is_rejected_on_update()
    {
        var id = await _repo.CreateAsync(1, "C", "d");

        var act = () => _repo.SetBooksAsync(id, 1, DndVersion.Edition2014, ["ALSO_NOT_A_BOOK"]);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*ALSO_NOT_A_BOOK*");
    }

    // Enum.TryParse (used by the Blazor circuit handlers) accepts any numeric string, so a crafted
    // out-of-range value would otherwise persist a permanently-empty MechanicsScope with no error.
    [Fact]
    public async Task An_out_of_range_edition_is_rejected_on_create()
    {
        var act = () => _repo.CreateAsync(1, "Bad Edition", "d", edition: (DndVersion)99, allowedBookKeys: ["PHB"]);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*99*");
    }

    [Fact]
    public async Task An_out_of_range_edition_is_rejected_on_update()
    {
        var id = await _repo.CreateAsync(1, "C", "d");

        var act = () => _repo.SetBooksAsync(id, 1, (DndVersion)99, ["PHB"]);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*99*");
    }

    // Malformed JSON in the AllowedBookKeys column (a corrupted row) must not throw out of EF's
    // materialization — the honest-empty policy — and must now also be logged loudly rather than
    // silently, so a corrupted row is distinguishable from a DM who unchecked every book.
    [Fact]
    public async Task Malformed_AllowedBookKeys_JSON_deserializes_to_an_empty_list_not_a_throw()
    {
        const string name = "Malformed row";
        await using (var db = pg.NewContext())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Campaigns" ("UserId", "Name", "Description", "CreatedAt", "Edition", "AllowedBookKeys")
                VALUES (1, {name}, 'd', now(), 0, 'not valid json')
                """);
        }

        var campaign = (await _repo.GetAllAsync(1)).Single(c => c.Name == name);
        var full = await _repo.GetByIdAsync(campaign.Id, 1);
        full!.AllowedBookKeys.Should().BeEmpty();
    }

    // A syntactically valid JSON array that contains a null element (e.g. '["PHB",null]') must not
    // reach CampaignScopeResolver.Resolve's HashSet<string>().Contains(null) or the value comparer's
    // hash — both would throw. The null is filtered rather than the whole row being treated as
    // malformed.
    [Fact]
    public async Task A_null_element_in_AllowedBookKeys_JSON_is_filtered_not_thrown()
    {
        const string name = "Null-element row";
        await using (var db = pg.NewContext())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Campaigns" ("UserId", "Name", "Description", "CreatedAt", "Edition", "AllowedBookKeys")
                VALUES (1, {name}, 'd', now(), 0, '["PHB",null]')
                """);
        }

        var campaign = (await _repo.GetAllAsync(1)).Single(c => c.Name == name);
        var full = await _repo.GetByIdAsync(campaign.Id, 1);
        full!.AllowedBookKeys.Should().BeEquivalentTo(["PHB"]);

        var scope = CampaignScopeResolver.Resolve(full);
        scope.LoreScope.Should().BeEquivalentTo(["PHB"]);
    }

    [Fact]
    public async Task SetBooksAsync_updates_owned_campaign_and_ignores_foreign()
    {
        var id = await _repo.CreateAsync(1, "C", "d");

        await _repo.SetBooksAsync(id, 1, DndVersion.Edition2024, ["XPHB", "ERLW"]);
        var mine = (await _repo.GetByIdAsync(id, 1))!;
        mine.Edition.Should().Be(DndVersion.Edition2024);
        mine.AllowedBookKeys.Should().BeEquivalentTo(["XPHB", "ERLW"]);

        await _repo.SetBooksAsync(id, userId: 2, DndVersion.Edition2014, ["PHB"]); // foreign — no-op
        (await _repo.GetByIdAsync(id, 1))!.AllowedBookKeys.Should().BeEquivalentTo(["XPHB", "ERLW"]);
    }
}