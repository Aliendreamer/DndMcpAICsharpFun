using DndMcpAICsharpFun.Features.Resolution;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Resolution;

public sealed class ArmorCatalogTests
{
    [Fact]
    public void Lookup_returns_base_ac_and_category()
    {
        ArmorCatalog.Lookup("Chain Mail").Should().Be((16, ArmorCategory.Heavy));
        ArmorCatalog.Lookup("Leather").Should().Be((11, ArmorCategory.Light));
        ArmorCatalog.Lookup("Half Plate").Should().Be((15, ArmorCategory.Medium));
    }

    [Fact]
    public void Lookup_is_case_insensitive()
        => ArmorCatalog.Lookup("chain mail").Should().Be((16, ArmorCategory.Heavy));

    [Fact]
    public void Lookup_unknown_is_null()
        => ArmorCatalog.Lookup("Mithral Plate").Should().BeNull();

    [Fact]
    public void Names_lists_the_catalog_for_the_dropdown()
        => ArmorCatalog.Names.Should().Contain("Plate").And.Contain("Leather").And.HaveCount(12);
}