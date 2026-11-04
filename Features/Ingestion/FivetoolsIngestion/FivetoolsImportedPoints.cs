using DndMcpAICsharpFun.Features.VectorStore.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;

/// <summary>
/// Points written by <c>POST /admin/5etools/import</c> carry the file hash <c>5etools:&lt;SOURCEKEY&gt;</c>.
/// Once a book is extracted programmatically those points are stale 5etools data and must go.
/// </summary>
public static class FivetoolsImportedPoints
{
    public static string FileHash(string sourceKey) => $"5etools:{sourceKey}";

    /// <summary>Deletes every <c>dnd_entities</c> point a 5etools import wrote for <paramref name="sourceKey"/>.</summary>
    public static Task PurgeAsync(IEntityVectorStore store, string sourceKey, CancellationToken ct) =>
        store.DeleteByFileHashAsync(FileHash(sourceKey.ToUpperInvariant()), ct); // 5etools source keys are upper-case
}
