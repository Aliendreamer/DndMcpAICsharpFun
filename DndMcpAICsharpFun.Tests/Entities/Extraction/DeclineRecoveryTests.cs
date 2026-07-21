using System.Text.Json;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using DndMcpAICsharpFun.Tests.TestDoubles;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using NSubstitute;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

public sealed class DeclineRecoveryTests
{
    private static EntityExtractionRunner BuildRunner(IEntityExtractionLlmClient llm, EntityNameMatcher? matcher = null) =>
        new(
            candidateExtractor: new CandidateExtractor(
                llm: llm,
                promptBuilder: new ExtractionPromptBuilder(),
                chunker: new SemanticChunker(),
                merger: new EntityFieldMerger(),
                retry: new ExtractionRetryPolicy { MaxAttempts = 1 },
                options: Options.Create(new EntityExtractionOptions { MaxOutputTokensPerEntity = 4096 }),
                ollamaOpts: Options.Create(new OllamaOptions()),
                logger: NullLogger<CandidateExtractor>.Instance),
            logger: NullLogger<EntityExtractionRunner>.Instance,
            cascade: GroundingCascadeTestFactory.Inert(),
            matcher: matcher);

    private static DndMcpAICsharpFun.Domain.IngestionRecord Record() => new()
    {
        Id = 1,
        FilePath = "/dev/null",
        FileName = "test.pdf",
        FileHash = "h",
        Version = "5e",
        DisplayName = "Test Book",
    };

    private static void ReturnsToolInput(IEntityExtractionLlmClient llm, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var clone = doc.RootElement.Clone();
        llm.ExtractAsync(Arg.Any<ExtractionRequest>(), Arg.Any<CancellationToken>())
           .Returns(new ExtractionResponse(
               Success: true, ToolInput: clone, StopReason: "tool_use",
               InputTokens: 0, OutputTokens: 0, ErrorMessage: null, RawJson: null));
    }

    private static EntityCandidate DeclinedCandidate() => new(
        Type: EntityType.Item,
        DisplayName: "Grappling Rules",
        Text: "When you attempt to grapple a creature, you use your action and one of your free " +
              "hands to make a grapple check contested by the target's ability check.",
        Page: 12,
        TypePrior: new[] { EntityType.Item });

    private static Dictionary<EntityType, JsonElement> Schemas() => new()
    {
        [EntityType.Rule] = JsonDocument.Parse("{}").RootElement.Clone(),
        [EntityType.Lore] = JsonDocument.Parse("{}").RootElement.Clone(),
    };

    [Fact]
    public async Task Recovery_pick_of_rule_that_grounds_returns_envelope_marked_decline_recovery()
    {
        var llm = Substitute.For<IEntityExtractionLlmClient>();
        ReturnsToolInput(llm, """{"entityType":"Rule","name":"Grapple"}""");

        var recovery = new DeclineRecovery(BuildRunner(llm), new ExtractionPromptBuilder(), matcher: null);

        var envelope = await recovery.TryRecoverAsync(
            Record(), DeclinedCandidate(), sourceBook: "Test", edition: "5e", schemas: Schemas(), ct: CancellationToken.None);

        envelope.Should().NotBeNull("a Rule pick that grounds must be recovered, not left declined");
        envelope!.Type.Should().Be(EntityType.Rule);
        envelope.DataSource.Should().Be("decline-recovery");
        envelope.Id.Should().Be(
            "test-book.rule.grappling-rules",
            "the recovered entity's id must be derived from its ACTUAL disposed type (Rule), not " +
            "the original declined type (Item) it was recorded under - test-book.item.grappling-rules " +
            "would be dishonest and could collide with a real Item entity of the same slug");
    }

    [Fact]
    public async Task Recovery_none_pick_stays_declined()
    {
        var llm = Substitute.For<IEntityExtractionLlmClient>();
        ReturnsToolInput(llm, """{"entityType":"none","reason":"pure heading, no real content"}""");

        var recovery = new DeclineRecovery(BuildRunner(llm), new ExtractionPromptBuilder(), matcher: null);

        var envelope = await recovery.TryRecoverAsync(
            Record(), DeclinedCandidate(), sourceBook: "Test", edition: "5e", schemas: Schemas(), ct: CancellationToken.None);

        envelope.Should().BeNull("a none pick means the candidate stays declined");
    }

    [Fact]
    public async Task Recovery_pick_with_ungrounded_fields_stays_declined()
    {
        // A genuine Rule/Lore pick (Success:true, entityType:Rule) whose only field value
        // ("Grappling") has no OCR-fuzzy support anywhere in the candidate's source text (which only
        // contains "grapple"/"grapple check" - Levenshtein distance 3, over the OCR-tolerance
        // threshold of 1). Tier 0 field-grounding therefore fails; with the extraction judge always
        // off (judgeEnabled: false in EntityExtractionRunner.BuildTypedEnvelope), the cascade can only
        // yield Uncertain, which ExtractionDispositionPolicy maps to NeedsReview - never Accepted.
        // Recovery must not admit this pick: an ungrounded recovery stays declined.
        var llm = Substitute.For<IEntityExtractionLlmClient>();
        ReturnsToolInput(llm, """{"entityType":"Rule","name":"Grappling"}""");

        var recovery = new DeclineRecovery(BuildRunner(llm), new ExtractionPromptBuilder(), matcher: null);

        var envelope = await recovery.TryRecoverAsync(
            Record(), DeclinedCandidate(), sourceBook: "Test", edition: "5e", schemas: Schemas(), ct: CancellationToken.None);

        envelope.Should().BeNull(
            "an ungrounded pick (fields not supported by the source text) must not be admitted, " +
            "even though the model returned a valid, successful Rule/Lore branch");
    }
}
