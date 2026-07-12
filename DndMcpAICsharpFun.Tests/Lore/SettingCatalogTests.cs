using DndMcpAICsharpFun.Features.Lore;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Lore;

public sealed class SettingCatalogTests
{
    [Fact]
    public void Eberron_resolves_to_its_book_plus_core()
    {
        var books = SettingCatalog.Resolve("Eberron");

        books.Should().Contain("Eberron: Rising from the Last War");
        books.Should().Contain(new[] { "PlayerHandbook 2014", "Dungeon Master's Guide 2014", "Monster Manual 2014" }); // core always included
    }

    [Fact]
    public void Unknown_or_generic_or_null_resolves_to_empty_unscoped()
    {
        SettingCatalog.Resolve(null).Should().BeEmpty();
        SettingCatalog.Resolve("").Should().BeEmpty();
        SettingCatalog.Resolve("Generic").Should().BeEmpty();
        SettingCatalog.Resolve("NotARealSetting").Should().BeEmpty();
    }

    [Fact]
    public void Resolve_is_case_insensitive_on_the_setting_key()
    {
        SettingCatalog.Resolve("eberron").Should().Contain("Eberron: Rising from the Last War");
    }

    [Fact]
    public void KnownSettings_lists_the_catalog_keys()
    {
        SettingCatalog.KnownSettings.Should().Contain("Eberron");
    }
}
