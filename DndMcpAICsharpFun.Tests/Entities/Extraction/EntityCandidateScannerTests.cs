using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

public class EntityCandidateScannerTests
{
    [Fact]
    public void Scanner_groups_by_section_and_maps_category_to_entity_type()
    {
        var toc = new TocCategoryMap(new[]
        {
            new TocSectionEntry("Lore", ContentCategory.Lore, StartPage: 1, EndPage: 30),
            new TocSectionEntry("Monsters", ContentCategory.Monster, StartPage: 31, EndPage: 200),
            new TocSectionEntry("Spells", ContentCategory.Spell, StartPage: 201, EndPage: 400),
        });

        var blocks = new List<ScannerInput>
        {
            new("Bullywug", 35, "Bullywug stat block text"),
            new("Bullywug", 36, "Bullywug actions"),
            new("Fireball", 241, "Fireball spell text"),
            new("History of the realms", 12, "Long lore prose"),
        };

        var scanner = new EntityCandidateScanner();
        var candidates = scanner.Scan(blocks, toc).ToList();

        candidates.Should().HaveCount(2);
        candidates.Should().ContainSingle(c => c.Type == EntityType.Monster && c.DisplayName == "Bullywug")
            .Which.Text.Should().Contain("Bullywug stat block text").And.Contain("Bullywug actions");
        candidates.Should().ContainSingle(c => c.Type == EntityType.Spell && c.DisplayName == "Fireball");
        candidates.Should().NotContain(c => c.DisplayName == "History of the realms");
    }

    [Fact]
    public void Lore_section_is_skipped()
    {
        var toc = new TocCategoryMap(new[]
        {
            new TocSectionEntry("Lore", ContentCategory.Lore, StartPage: 1, EndPage: 50),
        });

        var blocks = new List<ScannerInput>
        {
            new("History of the realms", 12, "Long lore prose"),
        };

        var scanner = new EntityCandidateScanner();
        var candidates = scanner.Scan(blocks, toc).ToList();

        candidates.Should().BeEmpty();
    }
}
