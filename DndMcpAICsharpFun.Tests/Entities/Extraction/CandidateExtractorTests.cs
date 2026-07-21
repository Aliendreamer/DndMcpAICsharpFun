using System.Text.Json;

using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

using FluentAssertions;

using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

public sealed class CandidateExtractorTests
{
    // ── StripConfidence ───────────────────────────────────────────────────────

    [Fact]
    public void StripConfidence_removes_confidence_property()
    {
        using var doc = JsonDocument.Parse(
            """{"name":"Goblin","cr":"1/4","confidence":"high"}""");

        var result = CandidateExtractor.StripConfidence(doc.RootElement.Clone());

        result.TryGetProperty("confidence", out _).Should().BeFalse("confidence must be stripped");
        result.TryGetProperty("name", out var name).Should().BeTrue();
        name.GetString().Should().Be("Goblin");
        result.TryGetProperty("cr", out var cr).Should().BeTrue();
        cr.GetString().Should().Be("1/4");
    }

    [Fact]
    public void StripConfidence_is_case_sensitive_and_leaves_Confidence_intact()
    {
        // "Confidence" (capital C) must NOT be stripped — field matching is ordinal.
        using var doc = JsonDocument.Parse("""{"Confidence":"medium","x":1}""");
        var result = CandidateExtractor.StripConfidence(doc.RootElement.Clone());

        result.TryGetProperty("Confidence", out _).Should().BeTrue("capital-C Confidence must be preserved");
    }

    [Fact]
    public void StripConfidence_handles_object_with_no_confidence_field()
    {
        using var doc = JsonDocument.Parse("""{"a":1,"b":"two"}""");
        var result = CandidateExtractor.StripConfidence(doc.RootElement.Clone());

        result.TryGetProperty("a", out var a).Should().BeTrue();
        a.GetInt32().Should().Be(1);
        result.TryGetProperty("b", out var b).Should().BeTrue();
        b.GetString().Should().Be("two");
    }

    // ── ExtractFieldsAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ExtractFieldsAsync_single_chunk_returns_llm_response_fields()
    {
        // Arrange
        var llm = Substitute.For<IEntityExtractionLlmClient>();
        using var fieldsDoc = JsonDocument.Parse("""{"name":"Aboleth","cr":"10"}""");
        llm.ExtractAsync(Arg.Any<ExtractionRequest>(), Arg.Any<CancellationToken>())
           .Returns(new ExtractionResponse(
               Success: true,
               ToolInput: fieldsDoc.RootElement.Clone(),
               StopReason: "tool_use",
               InputTokens: 0, OutputTokens: 0,
               ErrorMessage: null, RawJson: null));

        var opts = Options.Create(new EntityExtractionOptions { MaxOutputTokensPerEntity = 4096 });
        var ollamaOpts = Options.Create(new DndMcpAICsharpFun.Infrastructure.Ollama.OllamaOptions());
        var extractor = new CandidateExtractor(
            llm: llm,
            promptBuilder: new ExtractionPromptBuilder(),
            chunker: new SemanticChunker(),
            merger: new EntityFieldMerger(),
            retry: new ExtractionRetryPolicy { MaxAttempts = 1 },
            options: opts,
            ollamaOpts: ollamaOpts,
            logger: NullLogger<CandidateExtractor>.Instance);

        var record = new DndMcpAICsharpFun.Domain.IngestionRecord
        {
            Id = 1,
            FilePath = "/dev/null",
            FileName = "test.pdf",
            FileHash = "h",
            Version = "5e",
            DisplayName = "Test Book",
        };
        using var schema = JsonDocument.Parse("""{"type":"object"}""");
        var candidate = new EntityCandidate(
            Type: DndMcpAICsharpFun.Domain.Entities.EntityType.Monster,
            DisplayName: "Aboleth",
            Text: "Aboleth — a slimy aberration.",
            Page: 1);

        // Act
        var (fields, error) = await extractor.ExtractFieldsAsync(record, candidate, schema.RootElement.Clone(), CancellationToken.None);

        // Assert
        fields.Should().NotBeNull();
        error.Should().BeNull();
        fields!.Value.TryGetProperty("name", out var nameVal).Should().BeTrue();
        nameVal.GetString().Should().Be("Aboleth");
    }

    [Fact]
    public async Task ExtractFieldsAsync_returns_null_fields_and_error_when_llm_fails()
    {
        var llm = Substitute.For<IEntityExtractionLlmClient>();
        llm.ExtractAsync(Arg.Any<ExtractionRequest>(), Arg.Any<CancellationToken>())
           .Returns(new ExtractionResponse(
               Success: false,
               ToolInput: null,
               StopReason: "error",
               InputTokens: 0, OutputTokens: 0,
               ErrorMessage: "timeout", RawJson: null));

        var opts = Options.Create(new EntityExtractionOptions { MaxOutputTokensPerEntity = 4096 });
        var ollamaOpts = Options.Create(new DndMcpAICsharpFun.Infrastructure.Ollama.OllamaOptions());
        var extractor = new CandidateExtractor(
            llm: llm,
            promptBuilder: new ExtractionPromptBuilder(),
            chunker: new SemanticChunker(),
            merger: new EntityFieldMerger(),
            retry: new ExtractionRetryPolicy { MaxAttempts = 1 },
            options: opts,
            ollamaOpts: ollamaOpts,
            logger: NullLogger<CandidateExtractor>.Instance);

        var record = new DndMcpAICsharpFun.Domain.IngestionRecord
        {
            Id = 1,
            FilePath = "/dev/null",
            FileName = "test.pdf",
            FileHash = "h",
            Version = "5e",
            DisplayName = "Test Book",
        };
        using var schema = JsonDocument.Parse("""{"type":"object"}""");
        var candidate = new EntityCandidate(
            Type: DndMcpAICsharpFun.Domain.Entities.EntityType.Monster,
            DisplayName: "Beholder",
            Text: "Beholder text.",
            Page: 2);

        var (fields, error) = await extractor.ExtractFieldsAsync(record, candidate, schema.RootElement.Clone(), CancellationToken.None);

        fields.Should().BeNull();
        error.Should().Be("timeout");
    }

    // ── ExtractUnionAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExtractUnionAsync_union_call_sees_whole_candidate_not_just_first_chunk()
    {
        // Regression from re-extracting the Monster Manual: MM monster entries open with a lore
        // intro that fills the first chunk, so a union/type call limited to chunk[0] sees only
        // narrative and wrongly DECLINES. The type-decision call must see the stat block too.
        var capturedPrompts = new List<string>();
        var llm = Substitute.For<IEntityExtractionLlmClient>();
        using var resp = JsonDocument.Parse("""{"entityType":"Monster","name":"Gelatinous Cube"}""");
        llm.ExtractAsync(Arg.Any<ExtractionRequest>(), Arg.Any<CancellationToken>())
           .Returns(ci =>
           {
               capturedPrompts.Add(ci.Arg<ExtractionRequest>().UserPrompt);
               return new ExtractionResponse(
                   Success: true, ToolInput: resp.RootElement.Clone(), StopReason: "tool_use",
                   InputTokens: 0, OutputTokens: 0, ErrorMessage: null, RawJson: null);
           });

        var opts = Options.Create(new EntityExtractionOptions
        {
            MaxOutputTokensPerEntity = 4096,
            MaxTokensPerChunk = 20, // small, so the lore intro alone fills the first chunk
        });
        var ollamaOpts = Options.Create(new DndMcpAICsharpFun.Infrastructure.Ollama.OllamaOptions());
        var extractor = new CandidateExtractor(
            llm: llm, promptBuilder: new ExtractionPromptBuilder(), chunker: new SemanticChunker(),
            merger: new EntityFieldMerger(), retry: new ExtractionRetryPolicy { MaxAttempts = 1 },
            options: opts, ollamaOpts: ollamaOpts, logger: NullLogger<CandidateExtractor>.Instance);

        var record = new DndMcpAICsharpFun.Domain.IngestionRecord
        {
            Id = 1,
            FilePath = "/dev/null",
            FileName = "mm.pdf",
            FileHash = "h",
            Version = "5e",
            DisplayName = "Monster Manual",
        };
        var schemas = new Dictionary<DndMcpAICsharpFun.Domain.Entities.EntityType, JsonElement>
        {
            [DndMcpAICsharpFun.Domain.Entities.EntityType.Monster] =
                JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}}}""").RootElement.Clone(),
        };
        var lore = string.Join(" ", Enumerable.Repeat("lore", 80)); // long narrative intro
        var candidate = new EntityCandidate(
            Type: DndMcpAICsharpFun.Domain.Entities.EntityType.Monster,
            DisplayName: "Gelatinous Cube",
            Text: lore + " STATBLOCK_MARKER Armor Class 6 Hit Points 84 Challenge 2",
            Page: 1,
            TypePrior: new[] { DndMcpAICsharpFun.Domain.Entities.EntityType.Monster });

        var result = await extractor.ExtractUnionAsync(
            record, candidate, candidate.TypePrior, schemas, CancellationToken.None);

        result.Outcome.Should().Be(UnionOutcome.Typed);
        capturedPrompts.Should().NotBeEmpty();
        capturedPrompts[0].Should().Contain("STATBLOCK_MARKER",
            "the union/type-decision call must see the stat block, not just the lore-filled first chunk");
    }

    [Fact]
    public async Task ExtractUnionAsync_caps_type_decision_text_for_oversized_candidates()
    {
        // A huge candidate (e.g. a full PHB class section) must not be sent whole to the type call,
        // which would take minutes / fail. Only the top (cap) is sent; field extraction still chunks
        // the full text.
        var capturedPrompts = new List<string>();
        var llm = Substitute.For<IEntityExtractionLlmClient>();
        using var resp = JsonDocument.Parse("""{"entityType":"Class","name":"Wizard"}""");
        llm.ExtractAsync(Arg.Any<ExtractionRequest>(), Arg.Any<CancellationToken>())
           .Returns(ci =>
           {
               capturedPrompts.Add(ci.Arg<ExtractionRequest>().UserPrompt);
               return new ExtractionResponse(
                   Success: true, ToolInput: resp.RootElement.Clone(), StopReason: "tool_use",
                   InputTokens: 0, OutputTokens: 0, ErrorMessage: null, RawJson: null);
           });

        var opts = Options.Create(new EntityExtractionOptions
        {
            MaxOutputTokensPerEntity = 4096,
            MaxTokensPerChunk = 2000,
            MaxTypeDecisionChars = 200,
        });
        var ollamaOpts = Options.Create(new DndMcpAICsharpFun.Infrastructure.Ollama.OllamaOptions());
        var extractor = new CandidateExtractor(
            llm: llm, promptBuilder: new ExtractionPromptBuilder(), chunker: new SemanticChunker(),
            merger: new EntityFieldMerger(), retry: new ExtractionRetryPolicy { MaxAttempts = 1 },
            options: opts, ollamaOpts: ollamaOpts, logger: NullLogger<CandidateExtractor>.Instance);

        var record = new DndMcpAICsharpFun.Domain.IngestionRecord
        {
            Id = 1,
            FilePath = "/dev/null",
            FileName = "phb.pdf",
            FileHash = "h",
            Version = "5e",
            DisplayName = "Player's Handbook",
        };
        var schemas = new Dictionary<DndMcpAICsharpFun.Domain.Entities.EntityType, JsonElement>
        {
            [DndMcpAICsharpFun.Domain.Entities.EntityType.Class] =
                JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}}}""").RootElement.Clone(),
        };
        var candidate = new EntityCandidate(
            Type: DndMcpAICsharpFun.Domain.Entities.EntityType.Class,
            DisplayName: "Wizard",
            Text: "Wizard class features at the top. " + new string('x', 500) + " UNIQUE_TAIL_MARKER",
            Page: 1,
            TypePrior: new[] { DndMcpAICsharpFun.Domain.Entities.EntityType.Class });

        await extractor.ExtractUnionAsync(record, candidate, candidate.TypePrior, schemas, CancellationToken.None);

        capturedPrompts[0].Should().Contain("Wizard class features", "the top of the candidate is sent");
        capturedPrompts[0].Should().NotContain("UNIQUE_TAIL_MARKER",
            "text beyond MaxTypeDecisionChars must be excluded from the type-decision call");
    }


    [Fact]
    public async Task ExtractUnionAsync_systemPromptOverride_replaces_the_default_union_prompt()
    {
        // automatic-decline-recovery Task 1: an optional trailing systemPromptOverride lets a later
        // recovery pass re-classify declines with different framing, without changing any existing
        // caller (default null preserves BuildUnionSystemPrompt as before).
        var capturedSystemPrompts = new List<string>();
        var llm = Substitute.For<IEntityExtractionLlmClient>();
        using var resp = JsonDocument.Parse("""{"entityType":"Rule","name":"Grappling"}""");
        llm.ExtractAsync(Arg.Any<ExtractionRequest>(), Arg.Any<CancellationToken>())
           .Returns(ci =>
           {
               capturedSystemPrompts.Add(ci.Arg<ExtractionRequest>().SystemPrompt);
               return new ExtractionResponse(
                   Success: true, ToolInput: resp.RootElement.Clone(), StopReason: "tool_use",
                   InputTokens: 0, OutputTokens: 0, ErrorMessage: null, RawJson: null);
           });

        var opts = Options.Create(new EntityExtractionOptions { MaxOutputTokensPerEntity = 4096 });
        var ollamaOpts = Options.Create(new DndMcpAICsharpFun.Infrastructure.Ollama.OllamaOptions());
        var extractor = new CandidateExtractor(
            llm: llm, promptBuilder: new ExtractionPromptBuilder(), chunker: new SemanticChunker(),
            merger: new EntityFieldMerger(), retry: new ExtractionRetryPolicy { MaxAttempts = 1 },
            options: opts, ollamaOpts: ollamaOpts, logger: NullLogger<CandidateExtractor>.Instance);

        var record = new DndMcpAICsharpFun.Domain.IngestionRecord
        {
            Id = 1,
            FilePath = "/dev/null",
            FileName = "dmg.pdf",
            FileHash = "h",
            Version = "5e",
            DisplayName = "Dungeon Master's Guide",
        };
        var schemas = new Dictionary<DndMcpAICsharpFun.Domain.Entities.EntityType, JsonElement>
        {
            [DndMcpAICsharpFun.Domain.Entities.EntityType.Rule] =
                JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"}}}""").RootElement.Clone(),
        };
        var candidate = new EntityCandidate(
            Type: DndMcpAICsharpFun.Domain.Entities.EntityType.Rule,
            DisplayName: "Grappling",
            Text: "Grappling rules text.",
            Page: 1,
            TypePrior: new[] { DndMcpAICsharpFun.Domain.Entities.EntityType.Rule });

        const string overridePrompt = "RECOVERY_OVERRIDE_MARKER: classify as Rule or Lore.";

        var result = await extractor.ExtractUnionAsync(
            record, candidate, candidate.TypePrior, schemas, CancellationToken.None,
            systemPromptOverride: overridePrompt);

        result.Outcome.Should().Be(UnionOutcome.Typed);
        capturedSystemPrompts.Should().ContainSingle().Which.Should().Be(overridePrompt);
    }
}