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

        books.Should().Contain("ERLW");
        books.Should().Contain(new[] { "PHB", "DMG", "MM" }); // core always included
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
        SettingCatalog.Resolve("eberron").Should().Contain("ERLW");
    }

    [Fact]
    public void KnownSettings_lists_the_catalog_keys()
    {
        SettingCatalog.KnownSettings.Should().Contain("Eberron");
    }
}
