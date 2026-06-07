using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

public sealed class PartialJsonRecovererTests
{
    private readonly PartialJsonRecoverer _recoverer = new();

    [Fact]
    public void TryRecover_CompleteJson_ReturnsItWhole()
    {
        var raw = """{"name":"Fireball","level":3}""";
        _recoverer.TryRecover(raw, out var recovered).Should().BeTrue();
        recovered.Should().Be(raw);
    }

    [Fact]
    public void TryRecover_TruncatedMidObject_ReturnsFalse()
    {
        // Top-level { never closes — unrecoverable.
        var raw = """{"name":"Trap","variants":[{"name":"Pit","entries":["a"]}""";
        _recoverer.TryRecover(raw, out _).Should().BeFalse();
    }

    [Fact]
    public void TryRecover_TopLevelArrayTruncated_RecoversNothing()
    {
        var raw = """[{"name":"a"},{"name":"b"}""";
        _recoverer.TryRecover(raw, out _).Should().BeFalse();
    }

    [Fact]
    public void TryRecover_TrailingGarbageAfterValidJson_RecoversTheJson()
    {
        var raw = """{"name":"Fireball","level":3} and then the model rambled""";
        _recoverer.TryRecover(raw, out var recovered).Should().BeTrue();
        recovered.Should().Be("""{"name":"Fireball","level":3}""");
    }

    [Fact]
    public void TryRecover_NoJsonAtAll_ReturnsFalse()
    {
        _recoverer.TryRecover("the model returned prose", out _).Should().BeFalse();
    }

    [Fact]
    public void TryRecover_BracesInsideStrings_AreIgnored()
    {
        var raw = """{"entries":["use {@dc 15} here"],"name":"x"} trailing""";
        _recoverer.TryRecover(raw, out var recovered).Should().BeTrue();
        recovered.Should().Be("""{"entries":["use {@dc 15} here"],"name":"x"}""");
    }

    [Fact]
    public void TryRecover_EmptyString_ReturnsFalse()
    {
        _recoverer.TryRecover("", out _).Should().BeFalse();
    }
}
