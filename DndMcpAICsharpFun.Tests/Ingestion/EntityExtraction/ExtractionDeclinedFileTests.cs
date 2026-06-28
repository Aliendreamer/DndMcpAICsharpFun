using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Ingestion.EntityExtraction;

public sealed class ExtractionDeclinedFileTests
{
    [Fact]
    public async Task Writes_declined_list_and_deletes_when_empty()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "phb14.declined.json");
        var sut = new ExtractionDeclinedFile();
        await sut.WriteAsync(path, new List<DeclinedEntry>{ new("phb14.class.rage","Rage",EntityType.Class,"no_5etools_match") }, default);
        File.Exists(path).Should().BeTrue();
        var json = await File.ReadAllTextAsync(path);
        json.Should().Contain("no_5etools_match");
        json.Should().Contain("\"Class\"", because: "EntityType enum must serialize as string, not integer");
        await sut.WriteAsync(path, new List<DeclinedEntry>(), default);
        File.Exists(path).Should().BeFalse();
    }
}
