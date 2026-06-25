using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Ingestion.EntityExtraction;

public sealed class StatBlockScannerTests
{
    private static PdfStructureItem H(string text, int page = 1) => new("section_header", text, page, 1);
    private static PdfStructureItem T(string text, int page = 1) => new("text", text, page, null);

    private readonly StatBlockScanner _scanner = new();

    [Fact]
    public void Detects_header_clean_stat_block_with_section_name()
    {
        var items = new[]
        {
            H("GIANT FROG"),
            T("Medium beast, unaligned"),
            T("Armor Class 11  Hit Points 18 (4d8)  Speed 30ft."),
            T("STR DEX CON 12 13 11"),
        };

        var c = _scanner.Scan(items).Should().ContainSingle().Subject;
        c.Type.Should().Be(EntityType.Monster);
        c.DisplayName.Should().Be("GIANT FROG");
        c.Text.Should().Contain("Armor Class");
    }

    [Fact]
    public void Detects_headerless_stat_block_naming_from_nearest_lore_header()
    {
        // The size/type line has NO immediate header; the name is the lore-section header above.
        var items = new[]
        {
            H("CYCLOPS"),
            T("Cyclopes are reclusive giants."),
            T("They value gold and shells."),
            T("Huge giant, chaotic neutral", 2),
            T("Armor Class 14 (natural armor)  Hit Points 138 (12d12 + 60)", 2),
            T("STR 22 DEX 11 CON 20", 2),
        };

        var c = _scanner.Scan(items).Should().ContainSingle().Subject;
        c.DisplayName.Should().Be("CYCLOPS");
        c.Page.Should().Be(2);
        c.Text.Should().StartWith("Huge giant");
    }

    [Fact]
    public void Skips_internal_actions_header_when_naming()
    {
        var items = new[]
        {
            H("OGRE"),
            T("Ogres are hulking brutes."),
            H("ACTIONS"), // Marker mis-detects a stat-block sub-section as a header
            T("Large giant, chaotic evil"),
            T("Armor Class 11 (hide armor)  Hit Points 59 (7d10 + 21)"),
        };

        _scanner.Scan(items).Should().ContainSingle()
            .Which.DisplayName.Should().Be("OGRE", "the internal ACTIONS header must not become the name");
    }

    [Fact]
    public void Block_spans_internal_actions_header_and_ends_at_next_monster()
    {
        var items = new[]
        {
            H("HILL GIANT"),
            T("Hill giants are selfish brutes."),
            T("Huge giant, chaotic evil"),
            T("Armor Class 13 (natural armor)  Hit Points 105 (10d12 + 30)"),
            H("ACTIONS"),
            T("Multiattack. The giant makes two greatclub attacks."),
            H("STONE GIANT", 2),
            T("Huge giant, neutral", 2),
            T("Armor Class 17 (natural armor)  Hit Points 126", 2),
        };

        var cands = _scanner.Scan(items).ToList();
        cands.Should().HaveCount(2);
        var hill = cands[0];
        hill.DisplayName.Should().Be("HILL GIANT");
        hill.Text.Should().Contain("Multiattack", "the block must span the internal ACTIONS header");
        hill.Text.Should().NotContain("Stone giant", "the block must end at the next monster");
        cands[1].DisplayName.Should().Be("STONE GIANT");
    }

    [Fact]
    public void Ignores_non_stat_block_text()
    {
        var items = new[]
        {
            H("DRAGONS"),
            T("Dragons are the most ancient and dreaded of monsters."),
            T("A dragon's lair is a place of legend."),
        };

        _scanner.Scan(items).Should().BeEmpty();
    }

    [Fact]
    public void Requires_armor_class_near_the_size_type_line()
    {
        // A size/type-looking line with no Armor Class nearby is not a stat block.
        var items = new[]
        {
            H("LORE"),
            T("Large creatures roam the hills looking for prey and shelter."),
        };

        _scanner.Scan(items).Should().BeEmpty();
    }
}
