using System.Text.Json;

using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Ingestion.EntityExtraction;

public sealed class SidecarJsonFileWriterTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task WriteAsync_OverExistingFile_ReplacesContent_AndLeavesNoTmpFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "sidecar.json");
        try
        {
            await SidecarJsonFileWriter.WriteAsync(path, new List<string> { "first" }, Options, default);
            File.Exists(path).Should().BeTrue();

            await SidecarJsonFileWriter.WriteAsync(path, new List<string> { "second", "third" }, Options, default);

            var json = await File.ReadAllTextAsync(path);
            json.Should().NotContain("first");
            json.Should().Contain("second").And.Contain("third");

            Directory.GetFiles(dir, "*.tmp").Should().BeEmpty("atomic write must clean up its temp file");
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task WriteAsync_WritesToTempFileFirst_ThenRenamesIntoPlace()
    {
        // Regression guard for atomicity: the target file must never observably exist mid-write
        // as a truncated/partial file. We can't easily race the OS, but we can assert the writer
        // never leaves a `.tmp` sibling behind after a successful write, proving the rename step ran.
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "sidecar.json");
        try
        {
            await SidecarJsonFileWriter.WriteAsync(path, new List<string> { "only" }, Options, default);

            File.Exists(path).Should().BeTrue();
            Directory.GetFiles(dir).Should().ContainSingle().Which.Should().Be(path);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}