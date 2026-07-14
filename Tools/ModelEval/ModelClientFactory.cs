using Microsoft.Extensions.AI;

using OllamaSharp;
using OllamaSharp.Models.Chat;

namespace DndMcpAICsharpFun.Tools.ModelEval;

internal static class ModelClientFactory
{
    // OllamaApiClient (NOT MEAI.Ollama's OllamaChatClient) — only OllamaSharp can send the
    // top-level `think` field, and it implements MEAI IChatClient so the FunctionInvocation
    // stack wraps it identically to production.
    public static IChatClient Build(EvalArgs a) =>
        ((IChatClient)new OllamaApiClient(new Uri(a.BaseUrl), a.Model))
            .AsBuilder()
            .UseFunctionInvocation()
            .Build();

    /// <summary>
    /// Build the per-call ChatOptions. When think is OFF, set a RawRepresentationFactory that
    /// hands OllamaSharp a ChatRequest template with Think=false — its AbstractionMapper copies
    /// that into the outgoing request's top-level `think` field (spike: eval_count 388 -> 12).
    /// When think is ON, omit the factory (qwen3 thinks by default).
    /// </summary>
    public static ChatOptions BuildOptions(bool thinkOn, IList<AITool>? tools)
    {
        var options = new ChatOptions();
        if (tools is not null) options.Tools = [.. tools];
        if (!thinkOn) options.RawRepresentationFactory = _ => new ChatRequest { Think = false };
        return options;
    }
}
