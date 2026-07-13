using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Lore;      // CitedPassage
using DndMcpAICsharpFun.Features.Retrieval;

namespace DndMcpAICsharpFun.Features.Downtime;

/// <summary>
/// Retrieves downtime/crafting rule prose for a universal (non-campaign, non-user) activity, scoped to
/// the downtime source books so unrelated prose is excluded, and returns cited passages for the chat
/// persona to compose into a plan. Not ownership-gated. Does NOT call an LLM.
/// </summary>
public sealed class DowntimeService(IRagRetrievalService rag)
{
    public async Task<DowntimePlanResult> PlanAsync(string activity, DndVersion? edition, CancellationToken ct)
    {
        var query = new RetrievalQuery(
            activity, Version: edition, TopK: DowntimeSources.TopK, SourceKeys: DowntimeSources.Keys);
        var results = await rag.SearchAsync(query, ct);
        var passages = results.Select(r => new CitedPassage(
            r.Text, r.Metadata.SourceBook, r.Metadata.SectionTitle ?? r.Metadata.Chapter, r.Score)).ToList();
        return new DowntimePlanResult(passages, DndMcpAICsharpFun.Features.Retrieval.BookCatalog.ToDisplayNames(DowntimeSources.Keys));
    }
}
