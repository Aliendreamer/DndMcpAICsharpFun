using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

public sealed class DoclingDiskCache(
    IDoclingPdfConverter inner,
    IOptions<EntityExtractionOptions> options,
    ILogger<DoclingDiskCache> logger) : IDoclingPdfConverter
{
    private static readonly JsonSerializerOptions CacheJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<DoclingDocument> ConvertAsync(string filePath, CancellationToken ct = default)
    {
        var hash = ComputeFileHash(filePath);
        var cachePath = Path.Combine(options.Value.DoclingCacheDirectory, hash + ".json");

        try
        {
            await using var s = File.OpenRead(cachePath);
            var cached = await JsonSerializer.DeserializeAsync<DoclingDocument>(s, CacheJsonOptions, ct);
            if (cached is not null)
            {
                logger.LogInformation(
                    "Docling cache hit for {FileName} (hash {Hash})",
                    Path.GetFileName(filePath), hash[..8]);
                return cached;
            }
        }
        catch (FileNotFoundException) { }
        catch (JsonException ex)
        {
            logger.LogWarning(
                "Corrupt Docling cache file {CachePath}; deleting and re-converting: {Error}",
                cachePath, ex.Message);
            File.Delete(cachePath);
        }

        var doc = await inner.ConvertAsync(filePath, ct);
        await TryCacheAsync(cachePath, doc, ct);
        return doc;
    }

    private async Task TryCacheAsync(string cachePath, DoclingDocument doc, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(cachePath) ?? ".";
        Directory.CreateDirectory(dir);
        var tmp = cachePath + ".tmp";
        try
        {
            await using (var s = File.Create(tmp))
                await JsonSerializer.SerializeAsync(s, doc, CacheJsonOptions, ct);
            File.Move(tmp, cachePath, overwrite: true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex, "Failed to write Docling cache to {CachePath}; result returned uncached", cachePath);
            try { File.Delete(tmp); } catch { /* swallow cleanup */ }
        }
    }

    private static string ComputeFileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
