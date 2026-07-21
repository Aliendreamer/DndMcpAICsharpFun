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

    [Fact]
    public void Spell_prompt_contains_school_code_table()
    {
        var b = new ExtractionPromptBuilder();
        var prompt = b.BuildSystemPrompt("PHB", "Edition2014", EntityType.Spell);
        prompt.Should().Contain("V=Evocation")
              .And.Contain("C=Conjuration")
              .And.Contain("\"school\": \"V\"");
    }

    [Fact]
    public void Monster_prompt_contains_size_and_alignment_code_tables()
    {
        var b = new ExtractionPromptBuilder();
        var prompt = b.BuildSystemPrompt("MM", "Edition2014", EntityType.Monster);
        prompt.Should().Contain("M=Medium")
              .And.Contain("L=Lawful")
              .And.Contain("\"size\": [\"M\"]");
    }

    [Fact]
    public void All_prompts_contain_entries_format_guidance()
    {
        var b = new ExtractionPromptBuilder();
        foreach (var type in Enum.GetValues<EntityType>())
        {
            var prompt = b.BuildSystemPrompt("Test", "Edition2014", type);
            prompt.Should().Contain("entries", because: $"{type} prompt should mention entries array");
        }
    }

    [Fact]
    public void Rule_prompt_contains_ruleType_codes()
    {
        var b = new ExtractionPromptBuilder();
        var prompt = b.BuildSystemPrompt("DMG", "Edition2014", EntityType.Rule);
        prompt.Should().Contain("ruleType")
              .And.Contain("O=Optional")
              .And.Contain("V=Variant");
    }

    [Fact]
    public void God_prompt_contains_alignment_code_table()
    {
        var b = new ExtractionPromptBuilder();
        var prompt = b.BuildSystemPrompt("MTF", "Edition2014", EntityType.God);
        prompt.Should().Contain("L=Lawful")
              .And.Contain("G=Good")
              .And.Contain("\"alignment\": [\"L\", \"G\"]");
    }

    public static IEnumerable<object[]> AllEntityTypes =>
        Enum.GetValues<EntityType>().Select(t => new object[] { t });

    [Theory]
    [MemberData(nameof(AllEntityTypes))]
    public void System_prompt_includes_entity_type_classification_guidance(EntityType type)
    {
        var b = new ExtractionPromptBuilder();
        var prompt = b.BuildSystemPrompt("PHB", "Edition2014", type);
        prompt.Should().Contain("Subclass", because: "all prompts must list Subclass as a valid type");
        prompt.Should().Contain("Class is a last resort", because: "all prompts must warn against defaulting to Class");
    }

    [Fact]
    public void System_prompt_includes_title_case_naming_rule()
    {
        var b = new ExtractionPromptBuilder();
        var prompt = b.BuildSystemPrompt("Player's Handbook", "Edition2014", EntityType.Class);
        prompt.Should().Contain("title case");
        prompt.Should().Contain("ALL CAPS");
    }

    [Fact]
    public void BuildSystemPrompt_WithExampleForType_IncludesExampleOutput()
    {
        var examples = new Dictionary<EntityType, string>
        {
            [EntityType.Spell] = """{"name":"Fireball","level":3,"school":"V"}""",
        };
        var b = new ExtractionPromptBuilder(examples);

        var prompt = b.BuildSystemPrompt("PHB", "5e", EntityType.Spell);

        prompt.Should().Contain("Example output:");
        prompt.Should().Contain("\"name\":\"Fireball\"");
    }

    [Fact]
    public void BuildSystemPrompt_WithoutExampleForType_OmitsExampleSection()
    {
        var b = new ExtractionPromptBuilder();

        var prompt = b.BuildSystemPrompt("PHB", "5e", EntityType.Spell);

        prompt.Should().NotContain("Example output:");
    }


    [Fact]
    public void BuildRecoverySystemPrompt_frames_content_as_real_and_offers_rule_or_lore_classification()
    {
        // Recovery-pass framing (automatic-decline-recovery Task 1): distinct from the
        // entity-hunting union prompt that over-declines real rules/lore.
        var b = new ExtractionPromptBuilder();
        var prompt = b.BuildRecoverySystemPrompt("Dungeon Master's Guide", "Edition2014");

        prompt.Should().NotBeNullOrWhiteSpace();
        prompt.Should().Contain("Dungeon Master's Guide").And.Contain("Edition2014");
        prompt.Should().Contain("real");
        prompt.Should().Contain("official");
        prompt.Should().Contain("Rule");
        prompt.Should().Contain("Lore");
        prompt.Should().Contain("entityType:none");
    }
}