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
    public async Task<RulesRulingResult> AskAsync(
        string question, IReadOnlyList<string>? ruleTopics, DndVersion? edition, CancellationToken ct)
    {
        // Single-shot (v1) when the caller didn't decompose the question.
        if (ruleTopics is not { Count: > 0 })
        {
            var single = await RetrieveAsync(question, edition, RuleSources.TopK, ct);
            return new RulesRulingResult(single, DndMcpAICsharpFun.Features.Retrieval.BookCatalog.ToDisplayNames(RuleSources.Keys), []);
        }

        // Multi-hop: ground each named rule with its own scoped retrieval.
        var topicGroups = new List<RuleTopicPassages>(ruleTopics.Count);
        foreach (var topic in ruleTopics)
        {
            var topicPassages = await RetrieveAsync(topic, edition, RuleSources.TopicTopK, ct);
            topicGroups.Add(new RuleTopicPassages(topic, topicPassages));
        }

        // Deterministic safety net: `ruleTopics` decomposition is only ~80% reliable (the model can
        // drop a topic), so also retrieve on the whole question and fold it into the combined list
        // (not into topicGroups) so a dropped topic's rule can still surface.
        var whole = await RetrieveAsync(question, edition, RuleSources.TopK, ct);

        // Flat union de-duped by citation identity, keeping the highest-scoring copy; the per-topic
        // groups above still retain each passage under every rule it grounded.
        var merged = topicGroups
            .SelectMany(g => g.Passages)
            .Concat(whole)
            .GroupBy(p => (p.Text, p.SourceBook, p.Section))
            .Select(grp => grp.OrderByDescending(p => p.Score).First())
            .ToList();

        return new RulesRulingResult(merged, DndMcpAICsharpFun.Features.Retrieval.BookCatalog.ToDisplayNames(RuleSources.Keys), topicGroups);
    }

    private async Task<IReadOnlyList<CitedPassage>> RetrieveAsync(
        string query, DndVersion? edition, int topK, CancellationToken ct)
    {
        var q = new RetrievalQuery(query, Version: edition, TopK: topK, SourceKeys: RuleSources.Keys);
        var results = await rag.SearchAsync(q, ct);
        return results.Select(r => new CitedPassage(
            r.Text, r.Metadata.SourceBook, r.Metadata.SectionTitle ?? r.Metadata.Chapter, r.Score)).ToList();
    }
}
