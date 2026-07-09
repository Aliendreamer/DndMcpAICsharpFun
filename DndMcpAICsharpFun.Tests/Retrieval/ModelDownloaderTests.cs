using DndMcpAICsharpFun.Features.Retrieval;

namespace DndMcpAICsharpFun.Tests.Retrieval;

internal sealed class ThrowingMessageHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => throw new HttpRequestException("simulated network failure");
}

public sealed class ModelDownloaderTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private RerankerOptions MakeOpts(string modelUrl = "http://localhost:19999/model.onnx",
        string vocabUrl = "http://localhost:19999/vocab.txt") =>
        new()
        {
            Enabled = true,
            ModelPath = _tempDir,
            ModelUrl = modelUrl,
            VocabUrl = vocabUrl
        };

    [Fact]
    public async Task EnsureModelAsync_WhenBothFilesExist_ReturnsTrueWithoutDownload()
    {
        Directory.CreateDirectory(_tempDir);
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "model.onnx"), "stub");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "vocab.txt"), "stub");

        using var client = new HttpClient(new ThrowingMessageHandler());
        var result = await ModelDownloader.EnsureModelAsync(
            MakeOpts(), client, NullLogger.Instance, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task EnsureModelAsync_WhenDownloadFails_ReturnsFalseWithoutThrowing()
    {
        using var client = new HttpClient(new ThrowingMessageHandler());
        var result = await ModelDownloader.EnsureModelAsync(
            MakeOpts(), client, NullLogger.Instance, CancellationToken.None);

        Assert.False(result);
    }
}