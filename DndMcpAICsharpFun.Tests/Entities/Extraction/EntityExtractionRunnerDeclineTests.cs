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

public sealed class EntityExtractionRunnerDeclineTests
{
    private static EntityExtractionRunner BuildRunner(IEntityExtractionLlmClient llm, GroundingCascade? cascade = null) =>
        new(
            candidateExtractor: new CandidateExtractor(
                llm:           llm,
                promptBuilder: new ExtractionPromptBuilder(),
                chunker:       new SemanticChunker(),
                merger:        new EntityFieldMerger(),
                retry:         new ExtractionRetryPolicy { MaxAttempts = 1 },
                options:       Options.Create(new EntityExtractionOptions { MaxOutputTokensPerEntity = 4096 }),
                ollamaOpts:    Options.Create(new OllamaOptions()),
                logger:        NullLogger<CandidateExtractor>.Instance),
            logger: NullLogger<EntityExtractionRunner>.Instance,
            cascade: cascade ?? GroundingCascadeTestFactory.Inert());

    private static DndMcpAICsharpFun.Domain.IngestionRecord Record() => new()
    {
        Id = 1, FilePath = "/dev/null", FileName = "test.pdf",
        FileHash = "h", Version = "5e", DisplayName = "Test Book",
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

    [Fact]
    public async Task Model_decline_records_an_error_and_persists_no_entity()
    {
        // The model chose entityType:"none" and its `reason` is classification reasoning — exactly
        // the shape that used to be persisted as an empty Item shell with the reasoning as canonicalText.
        var llm = Substitute.For<IEntityExtractionLlmClient>();
        ReturnsToolInput(llm,
            """{"entityType":"none","reason":"the schema doesn't include an 'object' type, safer to classify as none"}""");

        var candidate = new EntityCandidate(
            Type: EntityType.Item, DisplayName: "Ballista", Text: "A Large object with AC and HP.",
            Page: 1, TypePrior: new[] { EntityType.Item });
        var schemas = new Dictionary<EntityType, JsonElement> { [EntityType.Item] = JsonDocument.Parse("{}").RootElement.Clone() };

        var (envelope, error) = await BuildRunner(llm).ExtractOneAsync(
            Record(), candidate, id: "test.item.ballista", sourceBook: "Test", edition: "5e",
            schemas: schemas, ct: CancellationToken.None);

        envelope.Should().BeNull("a declined candidate must NOT be persisted as an entity");
        error.Should().NotBeNull();
        error!.ErrorKind.Should().Be("extraction_declined");
        error.Detail.Should().Contain("object"); // the model reason is preserved for audit, not persisted as canonicalText
    }

    [Fact]
    public async Task Valid_typed_extraction_is_persisted_as_an_entity()
    {
        var llm = Substitute.For<IEntityExtractionLlmClient>();
        ReturnsToolInput(llm,
            """{"entityType":"Object","ac":[15],"hp":{"average":50,"formula":"unbroken"}}""");

        var candidate = new EntityCandidate(
            Type: EntityType.Object, DisplayName: "Ballista", Text: "A Large object. Armor Class 15. Hit Points 50.",
            Page: 1, TypePrior: new[] { EntityType.Object });
        var schemas = new Dictionary<EntityType, JsonElement> { [EntityType.Object] = JsonDocument.Parse("{}").RootElement.Clone() };

        var (envelope, error) = await BuildRunner(llm).ExtractOneAsync(
            Record(), candidate, id: "test.object.ballista", sourceBook: "Test", edition: "5e",
            schemas: schemas, ct: CancellationToken.None);

        error.Should().BeNull();
        envelope.Should().NotBeNull("a typed extraction with real fields must be persisted");
        envelope!.Type.Should().Be(EntityType.Object);
        envelope.Fields.TryGetProperty("ac", out _).Should().BeTrue();

        // Task 6: disposition now comes from the shared GroundingCascade (Tier 0 short-circuit) —
        // preserves the pre-cascade behavior for a Tier-0-grounded, confident, clean-named entity.
        envelope.Disposition.Should().Be(EntityDisposition.Accepted);
        envelope.NeedsReview.Should().BeFalse();
    }

    [Fact]
    public async Task Ungrounded_fields_with_judge_off_are_NeedsReview_not_Ungrounded()
    {
        // The model fabricates fields with no support anywhere in the source prose. Extraction time
        // keeps the grounding judge disabled (Task 7 threads the real per-run flag through), so the
        // cascade can only reach GroundingStatus.Uncertain here — never a judge-confirmed Ungrounded.
        // This preserves the pre-cascade HasGroundedContent-era behavior: ungrounded -> NeedsReview.
        var llm = Substitute.For<IEntityExtractionLlmClient>();
        ReturnsToolInput(llm,
            """{"entityType":"Object","ac":[999],"hp":{"average":12345,"formula":"totally-fabricated"}}""");

        var candidate = new EntityCandidate(
            Type: EntityType.Object, DisplayName: "Ballista", Text: "A Large object with no stats given here.",
            Page: 1, TypePrior: new[] { EntityType.Object });
        var schemas = new Dictionary<EntityType, JsonElement> { [EntityType.Object] = JsonDocument.Parse("{}").RootElement.Clone() };

        var (envelope, error) = await BuildRunner(llm).ExtractOneAsync(
            Record(), candidate, id: "test.object.ballista", sourceBook: "Test", edition: "5e",
            schemas: schemas, ct: CancellationToken.None);

        error.Should().BeNull();
        envelope.Should().NotBeNull();
        envelope!.Disposition.Should().Be(EntityDisposition.NeedsReview);
        envelope.NeedsReview.Should().BeTrue();
    }
}
