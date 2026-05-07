using DndMcpAICsharpFun.Features.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

public sealed class IntraBookReferenceClassifier(string currentBookSlug)
{
    private readonly string _prefix = currentBookSlug + ".";

    public bool IsIntraBook(string targetId) =>
        targetId.StartsWith(_prefix, StringComparison.Ordinal);

    public (IList<EntityReferenceWarning> Intra, IList<EntityReferenceWarning> Inter) Partition(
        IEnumerable<EntityReferenceWarning> warnings)
    {
        var intra = new List<EntityReferenceWarning>();
        var inter = new List<EntityReferenceWarning>();
        foreach (var w in warnings)
        {
            if (IsIntraBook(w.MissingTargetId)) intra.Add(w);
            else inter.Add(w);
        }
        return (intra, inter);
    }
}
