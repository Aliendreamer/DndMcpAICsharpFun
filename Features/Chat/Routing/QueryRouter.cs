using DndMcpAICsharpFun.Features.Embedding;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Chat.Routing;

/// <summary>
/// Pre-LLM query router (chat-query-router). Classifies a chat query to a tool group and returns the
/// narrowed tool set (that group's tools ∪ the always-safe core) when confident, else the full set
/// (the pre-router behavior). It only ever shapes the offered tool list — the LLM turn is unchanged.
/// A wrong/uncertain classification degrades safely: the always-safe core is always present, so the
/// model is never stranded.
/// </summary>
public sealed class QueryRouter(
    IEmbeddingService embeddings,
    IExemplarIndex exemplars,
    IOptions<QueryRouterOptions> options,
    ILogger<QueryRouter> logger)
{
    private readonly QueryRouterOptions _opts = options.Value;

    public async Task<IReadOnlyList<AITool>> RouteAsync(
        string query, IReadOnlyList<AITool> tools, CancellationToken ct)
    {
        if (!_opts.Enabled || tools.Count == 0) return tools;

        var (group, confidence, path) = await ClassifyAsync(query, ct);
        if (group is null || confidence < _opts.Threshold)
        {
            logger.LogDebug(
                "Query router: fallback (group={Group} conf={Conf:F3} path={Path}) → all {Total} tools",
                group ?? "(none)", confidence, path, tools.Count);
            return tools;
        }

        var offered = tools.Where(t => Keep(t, group)).ToList();
        logger.LogInformation(
            "Query router: {Path} → {Group} (conf={Conf:F3}) offering {Offered}/{Total} tools",
            path, group, confidence, offered.Count, tools.Count);
        return offered;
    }

    private async Task<(string? Group, double Confidence, string Path)> ClassifyAsync(
        string query, CancellationToken ct)
    {
        var signal = QuerySignals.Detect(query);
        if (signal is not null) return (signal, 1.0, "signal");

        var vecs = await embeddings.EmbedAsync([query], ct);
        if (vecs.Count == 0) return (null, 0, "fallback");
        var (group, cos) = await exemplars.ClassifyAsync(vecs[0], ct);
        return (group, cos, "embedding");
    }

    // Keep a tool when: it is not a named function (never hide), OR it is in the always-safe core,
    // OR it is unmapped (safe by default), OR its group matches the routed group.
    private bool Keep(AITool tool, string group)
    {
        if (tool is not AIFunction fn) return true;
        if (_opts.AlwaysSafeToolNames.Contains(fn.Name, StringComparer.Ordinal)) return true;
        if (!ToolGroups.Map.TryGetValue(fn.Name, out var g)) return true;
        return string.Equals(g, group, StringComparison.Ordinal);
    }
}
