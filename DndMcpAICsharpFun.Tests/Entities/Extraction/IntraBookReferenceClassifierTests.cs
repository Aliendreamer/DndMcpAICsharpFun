using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using FluentAssertions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction;

public class IntraBookReferenceClassifierTests
{
    [Fact]
    public void Same_book_prefix_is_intra_book()
    {
        var c = new IntraBookReferenceClassifier("phb14");
        c.IsIntraBook("phb14.subclass.battle-master").Should().BeTrue();
    }

    [Fact]
    public void Different_book_prefix_is_inter_book()
    {
        var c = new IntraBookReferenceClassifier("phb14");
        c.IsIntraBook("tasha.subclass.swashbuckler").Should().BeFalse();
    }

    [Fact]
    public void Partition_returns_intra_and_inter_lists()
    {
        var c = new IntraBookReferenceClassifier("phb14");
        var refs = new[]
        {
            new EntityReferenceWarning("phb14.class.fighter", "fields.subclasses[0]", "phb14.subclass.battle-master"),
            new EntityReferenceWarning("phb14.class.fighter", "fields.subclasses[1]", "tasha.subclass.psi-warrior"),
        };
        var (intra, inter) = c.Partition(refs);
        intra.Should().ContainSingle(r => r.MissingTargetId == "phb14.subclass.battle-master");
        inter.Should().ContainSingle(r => r.MissingTargetId == "tasha.subclass.psi-warrior");
    }
}
