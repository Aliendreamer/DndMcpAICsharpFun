namespace DndMcpAICsharpFun.Features.Retrieval;

public static class ModelDownloader
{
    public static async Task<bool> EnsureModelAsync(
        RerankerOptions opts, ILogger logger, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(opts.ModelPath);

            var modelPath = Path.Combine(opts.ModelPath, "model.onnx");
            var vocabPath = Path.Combine(opts.ModelPath, "vocab.txt");

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(10);

            if (!File.Exists(modelPath))
            {
                logger.LogInformation("Downloading reranker model from {Url}...", opts.ModelUrl);
                await DownloadFileAsync(client, opts.ModelUrl, modelPath, "model.onnx", logger, ct);
                logger.LogInformation("Reranker model downloaded to {Path}", modelPath);
            }
            else
            {
                logger.LogInformation("Reranker model already cached at {Path}", modelPath);
            }

            if (!File.Exists(vocabPath))
            {
                logger.LogInformation("Downloading tokenizer vocab from {Url}...", opts.VocabUrl);
                await DownloadFileAsync(client, opts.VocabUrl, vocabPath, "vocab.txt", logger, ct);
                logger.LogInformation("Tokenizer vocab downloaded to {Path}", vocabPath);
            }

            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Reranker model download failed: {Message}. Reranking disabled for this session.", ex.Message);
            return false;
        }
    }

    private static async Task DownloadFileAsync(
        HttpClient client, string url, string destPath, string label, ILogger logger, CancellationToken ct)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await using var file = File.Create(destPath);

        var buffer = new byte[81920];
        long downloaded = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct);
            downloaded += read;
            if (total.HasValue)
                logger.LogDebug("Downloading {Label}: {Pct}%", label, downloaded * 100 / total.Value);
        }
    }
}
