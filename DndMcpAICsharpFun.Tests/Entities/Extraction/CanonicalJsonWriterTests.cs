using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

public class CanonicalJsonWriterTests
{
    [Fact]
    public async Task Write_succeeds_atomically()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "test.json");
        try
        {
            var writer = new CanonicalJsonWriter();
            var file = new CanonicalJsonFile(
                SchemaVersion: "1",
                Book: new CanonicalBookMetadata("Test Book", "Edition2014", "deadbeef", "Test Book"),
                Entities: Array.Empty<EntityEnvelope>());

            await writer.WriteAsync(path, file, CancellationToken.None);

            File.Exists(path).Should().BeTrue();
            var content = await File.ReadAllTextAsync(path);
            content.Should().Contain("\"schemaVersion\": \"1\"");
            Directory.GetFiles(dir, "*.tmp").Should().BeEmpty();
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task Failed_write_leaves_no_partial_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "test.json");
        try
        {
            var writer = new CanonicalJsonWriter();
            // Create a directory at the target path so the rename fails
            Directory.CreateDirectory(path);

            var file = new CanonicalJsonFile("1",
                new CanonicalBookMetadata("X", "Edition2014", "h", "X"),
                Array.Empty<EntityEnvelope>());

            var act = () => writer.WriteAsync(path, file, CancellationToken.None).AsTask();
            await act.Should().ThrowAsync<Exception>();

            // No .tmp files left after the failed rename.
            Directory.GetFiles(dir, "*.tmp").Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            Directory.Delete(dir, true);
        }
    }
}
