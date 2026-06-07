using System.Security.Cryptography;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;

namespace DndMcpAICsharpFun.Tests.Ingestion.Pdf;

public sealed class PdfConversionDiskCacheTests
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

            var expected = new PdfStructureDocument("# Hello", [new PdfStructureItem("text", "Hello", 1, null)]);
            var inner = Substitute.For<IPdfStructureConverter>();
            inner.ConvertAsync(pdfPath, Arg.Any<CancellationToken>()).Returns(expected);

            var opts = Options.Create(new EntityExtractionOptions { ConversionCacheDirectory = dir });
            var cache = new PdfConversionDiskCache(inner, opts, NullLogger<PdfConversionDiskCache>.Instance);

            var result = await cache.ConvertAsync(pdfPath);

            Assert.Equal(expected.Markdown, result.Markdown);
            Assert.Single(result.Items);
            await inner.Received(1).ConvertAsync(pdfPath, Arg.Any<CancellationToken>());

            var cacheFile = Path.Combine(dir, HexHash(pdfBytes) + ".marker.json");
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

            var expected = new PdfStructureDocument("# Hello", [new PdfStructureItem("text", "Hello", 1, null)]);
            var inner = Substitute.For<IPdfStructureConverter>();
            inner.ConvertAsync(pdfPath, Arg.Any<CancellationToken>()).Returns(expected);

            var opts = Options.Create(new EntityExtractionOptions { ConversionCacheDirectory = dir });
            var cache = new PdfConversionDiskCache(inner, opts, NullLogger<PdfConversionDiskCache>.Instance);

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
            var corruptPath = Path.Combine(dir, HexHash(pdfBytes) + ".marker.json");
            await File.WriteAllTextAsync(corruptPath, "NOT VALID JSON {{{{");

            var expected = new PdfStructureDocument("# Hello", []);
            var inner = Substitute.For<IPdfStructureConverter>();
            inner.ConvertAsync(pdfPath, Arg.Any<CancellationToken>()).Returns(expected);

            var opts = Options.Create(new EntityExtractionOptions { ConversionCacheDirectory = dir });
            var cache = new PdfConversionDiskCache(inner, opts, NullLogger<PdfConversionDiskCache>.Instance);

            var result = await cache.ConvertAsync(pdfPath);

            // Verify the corrupt file was replaced with a valid cache entry
            var json = await File.ReadAllTextAsync(corruptPath);
            var rehydrated = System.Text.Json.JsonSerializer.Deserialize<PdfStructureDocument>(json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
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

            var expected = new PdfStructureDocument("# Hello", []);
            var inner = Substitute.For<IPdfStructureConverter>();
            inner.ConvertAsync(pdfPath, Arg.Any<CancellationToken>()).Returns(expected);

            var opts = Options.Create(new EntityExtractionOptions { ConversionCacheDirectory = nonExistentDir });
            var cache = new PdfConversionDiskCache(inner, opts, NullLogger<PdfConversionDiskCache>.Instance);

            var result = await cache.ConvertAsync(pdfPath);

            Assert.Equal(expected.Markdown, result.Markdown);
            await inner.Received(1).ConvertAsync(pdfPath, Arg.Any<CancellationToken>());
            // Cache dir should now be created by TryCacheAsync
            Assert.True(Directory.Exists(nonExistentDir));
        }
        finally { Directory.Delete(pdfDir, true); }
    }

    [Fact]
    public async Task MarkerSuffix_CacheFileWrittenWithMarkerJson()
    {
        // Verifies that the cache is written as <hash>.marker.json, not <hash>.json.
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
            var pdfPath = Path.Combine(dir, "test.pdf");
            await File.WriteAllBytesAsync(pdfPath, pdfBytes);

            var expected = new PdfStructureDocument("# Marker", [new PdfStructureItem("text", "Marker", 1, null)]);
            var inner = Substitute.For<IPdfStructureConverter>();
            inner.ConvertAsync(pdfPath, Arg.Any<CancellationToken>()).Returns(expected);

            var opts = Options.Create(new EntityExtractionOptions { ConversionCacheDirectory = dir });
            var cache = new PdfConversionDiskCache(inner, opts, NullLogger<PdfConversionDiskCache>.Instance);

            await cache.ConvertAsync(pdfPath);

            var markerCacheFile = Path.Combine(dir, HexHash(pdfBytes) + ".marker.json");
            var legacyCacheFile = Path.Combine(dir, HexHash(pdfBytes) + ".json");

            Assert.True(File.Exists(markerCacheFile), ".marker.json cache file must exist");
            Assert.False(File.Exists(legacyCacheFile), "Legacy .json cache file must NOT be written");
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task LegacyCacheFile_IsIgnored_InnerConverterCalled()
    {
        // If only <hash>.json (legacy cache without .marker.json suffix) exists, the cache must treat it as a miss
        // and call the inner converter.
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        try
        {
            var pdfBytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
            var pdfPath = Path.Combine(dir, "test.pdf");
            await File.WriteAllBytesAsync(pdfPath, pdfBytes);

            // Write a syntactically valid JSON at the legacy path.
            var legacyDoc = new PdfStructureDocument("# Legacy", [new PdfStructureItem("text", "Legacy", 1, null)]);
            var legacyJson = System.Text.Json.JsonSerializer.Serialize(legacyDoc,
                new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
            var legacyCachePath = Path.Combine(dir, HexHash(pdfBytes) + ".json");
            await File.WriteAllTextAsync(legacyCachePath, legacyJson);

            var fresh = new PdfStructureDocument("# Fresh", []);
            var inner = Substitute.For<IPdfStructureConverter>();
            inner.ConvertAsync(pdfPath, Arg.Any<CancellationToken>()).Returns(fresh);

            var opts = Options.Create(new EntityExtractionOptions { ConversionCacheDirectory = dir });
            var cache = new PdfConversionDiskCache(inner, opts, NullLogger<PdfConversionDiskCache>.Instance);

            var result = await cache.ConvertAsync(pdfPath);

            // Must return fresh result (from inner), not the legacy cached value.
            Assert.Equal("# Fresh", result.Markdown);
            await inner.Received(1).ConvertAsync(pdfPath, Arg.Any<CancellationToken>());
        }
        finally { Directory.Delete(dir, true); }
    }
}
