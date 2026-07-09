using DndMcpAICsharpFun.Features.Retrieval;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Retrieval;

public sealed class RerankerOptionsTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var opts = new RerankerOptions();

        opts.Enabled.Should().BeTrue();
        opts.RerankBlocks.Should().BeTrue();
        opts.RerankEntities.Should().BeTrue();
        opts.CandidatePoolSize.Should().Be(20);
    }

    [Fact]
    public async Task EnabledFalse_DisablesBothChannels_ViaRerankingService()
    {
        var mockReranker = Substitute.For<IReranker>();
        mockReranker.Enabled.Returns(true);

        var opts = new RerankerOptions { Enabled = false, RerankBlocks = true, RerankEntities = true };
        var svc = new RerankingService(mockReranker, Options.Create(opts));

        var candidates = new[] { "a", "b" };
        await svc.RerankAsync("q", candidates, s => s, 2, CancellationToken.None);

        // No rerank calls regardless of per-channel flags
        await mockReranker.DidNotReceive()
            .RerankAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
    }
}