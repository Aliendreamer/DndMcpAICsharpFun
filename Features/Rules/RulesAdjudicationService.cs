using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Lore;      // CitedPassage
using DndMcpAICsharpFun.Features.Retrieval;

namespace DndMcpAICsharpFun.Features.Rules;

/// <summary>
/// Retrieves rule prose for a universal (non-campaign, non-user) rules question, scoped to the core
/// rulebooks so monster/lore prose is excluded, and returns cited passages for the chat persona to
/// frame into a ruling. Not ownership-gated. Does NOT call an LLM.
/// </summary>
public sealed class RulesAdjudicationService(IRagRetrievalService rag)
{
    public async Task<RulesRulingResult> AskAsync(string question, DndVersion? edition, CancellationToken ct)
    {
        var query = new RetrievalQuery(
            question, Version: edition, TopK: RuleSources.TopK, SourceBooks: RuleSources.Books);

        var results = await rag.SearchAsync(query, ct);

        var passages = results.Select(r => new CitedPassage(
            r.Text,
            r.Metadata.SourceBook,
            r.Metadata.SectionTitle ?? r.Metadata.Chapter,
            r.Score)).ToList();

        return new RulesRulingResult(passages, RuleSources.Books);
    }
}
