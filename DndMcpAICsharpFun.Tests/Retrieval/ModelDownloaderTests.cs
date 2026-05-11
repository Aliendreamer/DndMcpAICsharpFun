using DndMcpAICsharpFun.Features.Retrieval;

namespace DndMcpAICsharpFun.Tests.Retrieval;

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

        var result = await ModelDownloader.EnsureModelAsync(
            MakeOpts(), NullLogger.Instance, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task EnsureModelAsync_WhenDownloadFails_ReturnsFalseWithoutThrowing()
    {
        var result = await ModelDownloader.EnsureModelAsync(
            MakeOpts(), NullLogger.Instance, CancellationToken.None);

        Assert.False(result);
    }
}
