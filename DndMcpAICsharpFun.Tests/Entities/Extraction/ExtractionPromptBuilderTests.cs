using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

public class ExtractionPromptBuilderTests
{
    [Fact]
    public void Builds_system_prompt_naming_book_and_type()
    {
        var b = new ExtractionPromptBuilder();
        var prompt = b.BuildSystemPrompt("Player's Handbook 2014", "Edition2014", EntityType.Class);
        prompt.Should().Contain("Player's Handbook 2014").And.Contain("Edition2014").And.Contain("Class");
        prompt.Should().Contain("emit_class_fields");
    }

    [Fact]
    public void User_prompt_includes_candidate_text_verbatim()
    {
        var b = new ExtractionPromptBuilder();
        var candidate = new EntityCandidate(EntityType.Monster, "Bullywug", "Bullywug stat block text...", Page: 35);
        var prompt = b.BuildUserPrompt(candidate);
        prompt.Should().Contain("Bullywug").And.Contain("Bullywug stat block text...");
    }

    [Fact]
    public void Tool_name_is_lowercase_snake_case()
    {
        new ExtractionPromptBuilder().ToolName(EntityType.MagicItem).Should().Be("emit_magic_item_fields");
        new ExtractionPromptBuilder().ToolName(EntityType.DiseasePoison).Should().Be("emit_disease_poison_fields");
    }
}
