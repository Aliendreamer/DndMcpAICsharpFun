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

        var opts       = Options.Create(new EntityExtractionOptions { MaxOutputTokensPerEntity = 4096 });
        var ollamaOpts = Options.Create(new DndMcpAICsharpFun.Infrastructure.Ollama.OllamaOptions());
        var extractor  = new CandidateExtractor(
            llm:           llm,
            promptBuilder: new ExtractionPromptBuilder(),
            chunker:       new SemanticChunker(),
            merger:        new EntityFieldMerger(),
            retry:         new ExtractionRetryPolicy { MaxAttempts = 1 },
            options:       opts,
            ollamaOpts:    ollamaOpts,
            logger:        NullLogger<CandidateExtractor>.Instance);

        var record = new DndMcpAICsharpFun.Domain.IngestionRecord
        {
            Id = 1, FilePath = "/dev/null", FileName = "test.pdf",
            FileHash = "h", Version = "5e", DisplayName = "Test Book",
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

        var opts       = Options.Create(new EntityExtractionOptions { MaxOutputTokensPerEntity = 4096 });
        var ollamaOpts = Options.Create(new DndMcpAICsharpFun.Infrastructure.Ollama.OllamaOptions());
        var extractor  = new CandidateExtractor(
            llm:           llm,
            promptBuilder: new ExtractionPromptBuilder(),
            chunker:       new SemanticChunker(),
            merger:        new EntityFieldMerger(),
            retry:         new ExtractionRetryPolicy { MaxAttempts = 1 },
            options:       opts,
            ollamaOpts:    ollamaOpts,
            logger:        NullLogger<CandidateExtractor>.Instance);

        var record = new DndMcpAICsharpFun.Domain.IngestionRecord
        {
            Id = 1, FilePath = "/dev/null", FileName = "test.pdf",
            FileHash = "h", Version = "5e", DisplayName = "Test Book",
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
}
