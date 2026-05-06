using System.Security.Cryptography;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;

namespace DndMcpAICsharpFun.Tests.Ingestion.Pdf;

public sealed class DoclingDiskCacheTests
{
    private static string HexHash(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    [Fact]
    public async Task CacheMiss_CallsInner_AndWritesCacheFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 }; // %PDF
            var pdfPath = Path.Combine(dir, "test.pdf");
            await File.WriteAllBytesAsync(pdfPath, pdfBytes);

            var expected = new DoclingDocument("# Hello", [new DoclingItem("text", "Hello", 1, null)]);
            var inner = Substitute.For<IDoclingPdfConverter>();
            inner.ConvertAsync(pdfPath, Arg.Any<CancellationToken>()).Returns(expected);

            var opts = Options.Create(new EntityExtractionOptions { DoclingCacheDirectory = dir });
            var cache = new DoclingDiskCache(inner, opts, NullLogger<DoclingDiskCache>.Instance);

            var result = await cache.ConvertAsync(pdfPath);

            Assert.Equal(expected.Markdown, result.Markdown);
            Assert.Single(result.Items);
            await inner.Received(1).ConvertAsync(pdfPath, Arg.Any<CancellationToken>());

            var cacheFile = Path.Combine(dir, HexHash(pdfBytes) + ".json");
            Assert.True(File.Exists(cacheFile));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task CacheHit_ReturnsFromDisk_DoesNotCallInner()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
            var pdfPath = Path.Combine(dir, "test.pdf");
            await File.WriteAllBytesAsync(pdfPath, pdfBytes);

            var expected = new DoclingDocument("# Hello", [new DoclingItem("text", "Hello", 1, null)]);
            var inner = Substitute.For<IDoclingPdfConverter>();
            inner.ConvertAsync(pdfPath, Arg.Any<CancellationToken>()).Returns(expected);

            var opts = Options.Create(new EntityExtractionOptions { DoclingCacheDirectory = dir });
            var cache = new DoclingDiskCache(inner, opts, NullLogger<DoclingDiskCache>.Instance);

            await cache.ConvertAsync(pdfPath);       // miss — writes cache
            var result = await cache.ConvertAsync(pdfPath); // hit — reads cache

            Assert.Equal(expected.Markdown, result.Markdown);
            await inner.Received(1).ConvertAsync(pdfPath, Arg.Any<CancellationToken>()); // called once only
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task CorruptCacheFile_DeletesAndCallsInnerAgain()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
            var pdfPath = Path.Combine(dir, "test.pdf");
            await File.WriteAllBytesAsync(pdfPath, pdfBytes);

            // Pre-create a corrupt cache file with the correct hash name
            var corruptPath = Path.Combine(dir, HexHash(pdfBytes) + ".json");
            await File.WriteAllTextAsync(corruptPath, "NOT VALID JSON {{{{");

            var expected = new DoclingDocument("# Hello", []);
            var inner = Substitute.For<IDoclingPdfConverter>();
            inner.ConvertAsync(pdfPath, Arg.Any<CancellationToken>()).Returns(expected);

            var opts = Options.Create(new EntityExtractionOptions { DoclingCacheDirectory = dir });
            var cache = new DoclingDiskCache(inner, opts, NullLogger<DoclingDiskCache>.Instance);

            var result = await cache.ConvertAsync(pdfPath);

            // Verify the corrupt file was replaced with a valid cache entry
            var json = await File.ReadAllTextAsync(corruptPath);
            var rehydrated = System.Text.Json.JsonSerializer.Deserialize<DoclingDocument>(json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            Assert.NotNull(rehydrated);
            Assert.Equal(expected.Markdown, rehydrated!.Markdown);

            Assert.Equal(expected.Markdown, result.Markdown);
            await inner.Received(1).ConvertAsync(pdfPath, Arg.Any<CancellationToken>());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task NonExistentCacheDirectory_TreatedAsCacheMiss()
    {
        var pdfDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(pdfDir);
        try
        {
            var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
            var pdfPath = Path.Combine(pdfDir, "test.pdf");
            await File.WriteAllBytesAsync(pdfPath, pdfBytes);

            // Non-existent directory — the directory does not exist at all.
            var nonExistentDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            var expected = new DoclingDocument("# Hello", []);
            var inner = Substitute.For<IDoclingPdfConverter>();
            inner.ConvertAsync(pdfPath, Arg.Any<CancellationToken>()).Returns(expected);

            var opts = Options.Create(new EntityExtractionOptions { DoclingCacheDirectory = nonExistentDir });
            var cache = new DoclingDiskCache(inner, opts, NullLogger<DoclingDiskCache>.Instance);

            var result = await cache.ConvertAsync(pdfPath);

            Assert.Equal(expected.Markdown, result.Markdown);
            await inner.Received(1).ConvertAsync(pdfPath, Arg.Any<CancellationToken>());
            // Cache dir should now be created by TryCacheAsync
            Assert.True(Directory.Exists(nonExistentDir));
        }
        finally { Directory.Delete(pdfDir, true); }
    }
}
