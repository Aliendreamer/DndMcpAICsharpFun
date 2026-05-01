using DndMcpAICsharpFun.Features.Ingestion;

namespace DndMcpAICsharpFun.Tests.Ingestion;

public class ExtractionCancellationRegistryTests
{
    [Fact]
    public void Cancel_ReturnsFalse_WhenNothingRegistered()
    {
        var registry = new ExtractionCancellationRegistry();
        Assert.False(registry.Cancel(42));
    }

    [Fact]
    public void Cancel_ReturnsTrue_AndCancelsToken_WhenRegistered()
    {
        var registry = new ExtractionCancellationRegistry();
        using var cts = new CancellationTokenSource();
        registry.Register(1, cts);

        var result = registry.Cancel(1);

        Assert.True(result);
        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public void Cancel_ReturnsFalse_AfterUnregister()
    {
        var registry = new ExtractionCancellationRegistry();
        using var cts = new CancellationTokenSource();
        registry.Register(1, cts);
        registry.Unregister(1);

        Assert.False(registry.Cancel(1));
    }

    [Fact]
    public void Register_OverwritesPrevious_WhenSameBookId()
    {
        var registry = new ExtractionCancellationRegistry();
        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();

        registry.Register(1, cts1);
        registry.Register(1, cts2);
        registry.Cancel(1);

        Assert.True(cts2.IsCancellationRequested);
        Assert.False(cts1.IsCancellationRequested);
    }
}
