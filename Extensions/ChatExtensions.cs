using DndMcpAICsharpFun.Features.CharacterAdvice;
using DndMcpAICsharpFun.Features.Chat;
using DndMcpAICsharpFun.Features.Downtime;
using DndMcpAICsharpFun.Features.Lore;
using DndMcpAICsharpFun.Features.Npc;
using DndMcpAICsharpFun.Features.Rules;
using DndMcpAICsharpFun.Features.SessionPrep;

using Microsoft.Extensions.AI;

using OllamaSharp;
using OllamaSharp.Models;

namespace DndMcpAICsharpFun.Extensions;

internal static class ChatExtensions
{
    internal static IServiceCollection AddDndChat(this IServiceCollection services, IConfiguration config)
    {
        // DndChatService depends on EncounterDesignService (and now SettingLoreService and
        // RulesAdjudicationService); pulling AddEncounters()/AddLore()/AddRules() in here keeps those
        // dependencies self-contained regardless of composition order/omission (idempotent — these
        // only register services, no options validation side effects).
        services.AddEncounters();
        services.AddCharacterAdvice();
        services.AddLore();
        services.AddRules();
        services.AddDowntime();
        services.AddNpc();
        services.AddSessionPrep();

        services.AddOptions<McpClientOptions>()
            .BindConfiguration("McpClient")
            .ValidateOnStart();

        var baseUrl = config["Ollama:BaseUrl"] ?? "http://ollama:11434";
        var chatModel = config["Ollama:ChatModel"] ?? "qwen3:8b";
        services.AddOptions<ChatPersonaOptions>()
            .BindConfiguration("Chat");
        services.AddSingleton<PersonaProvider>();
        services.AddHttpContextAccessor();
        services.AddSingleton<McpToolsProvider>();
        services.AddSingleton<IMcpToolsProvider>(sp => sp.GetRequiredService<McpToolsProvider>());
        services.AddTransient<IChatClient>(sp =>
        {
            // OllamaSharp's OllamaApiClient (NOT MEAI.Ollama's OllamaChatClient) — only it can send
            // the top-level `think` field that DndChatService uses to run qwen3 think-off. It
            // implements MEAI IChatClient, so the FunctionInvocation stack wraps it identically.
            // Explicit HttpClient: the Uri constructor gets the .NET default 100 s timeout, which cut
            // long tool-calling turns (the level-up coach) off mid-answer.
            var chatHttp = Infrastructure.Http.LongRunningHttpClient.Create(
                new Uri(baseUrl), config.GetValue("Ollama:ChatTimeoutSeconds", 600));
            var defaults = new ChatSampling();
            var sampling = new ChatSampling(
                config.GetValue("Ollama:ChatTemperature", defaults.Temperature),
                config.GetValue("Ollama:ChatTopP", defaults.TopP),
                config.GetValue("Ollama:ChatTopK", defaults.TopK),
                config.GetValue("Ollama:ChatSeed", defaults.Seed),
                config.GetValue("Ollama:ChatMaxOutputTokens", defaults.MaxOutputTokens));
            return BuildChatClient(chatHttp, chatModel, config.GetValue("Ollama:ChatContextTokens", 12288), sampling,
                sp.GetService<Microsoft.Extensions.Logging.ILoggerFactory>());
        });
        // chat-query-router: pre-LLM tool-set narrowing. Options bind from "ChatQueryRouter"
        // (env overrides live; appsettings git-crypt-masked). ExemplarIndex is a singleton so the
        // per-group exemplar centroids are embedded exactly once for the process.
        services.AddOptions<Features.Chat.Routing.QueryRouterOptions>()
            .BindConfiguration("ChatQueryRouter");
        services.AddSingleton<Features.Chat.Routing.IExemplarIndex, Features.Chat.Routing.ExemplarIndex>();
        services.AddScoped<Features.Chat.Routing.QueryRouter>();
        services.AddScoped<DndChatService>();
        services.AddScoped<Features.Admin.IEvalTurnRunner, Features.Admin.DndChatEvalTurnRunner>();
        services.AddScoped<Features.Admin.IEvalUserResolver, Features.Admin.RepositoryEvalUserResolver>();
        return services;
    }

    /// <summary>
    /// Tool rounds per model call; real turns need at most 2-3. Without a cap the library allows 40.
    /// </summary>
    internal const int MaxToolRounds = 4;

    /// <summary>
    /// Sampling for qwen3 thinking mode, as its model card asks: temperature 0.6, top-p 0.95, top-k 20,
    /// min-p 0. Greedy decoding (temperature 0) made about 2% of calls repeat until the 5-minute timeout.
    /// A fixed seed keeps eval runs repeatable; MaxOutputTokens stops a runaway call.
    /// </summary>
    internal sealed record ChatSampling(
        float Temperature = 0.6f, float TopP = 0.95f, int TopK = 20, long? Seed = 42, int MaxOutputTokens = 4096);

    /// <summary>
    /// The chat's model client: tool calls run in-process, and every request (tool rounds too) asks for
    /// <paramref name="contextTokens"/> of context instead of Ollama's GPU-based default.
    /// </summary>
    internal static IChatClient BuildChatClient(
        HttpClient http, string model, int contextTokens, ChatSampling? sampling = null,
        Microsoft.Extensions.Logging.ILoggerFactory? loggerFactory = null)
    {
        var s = sampling ?? new ChatSampling();
        IChatClient inner = new OllamaApiClient(http, model);
        return inner.AsBuilder()
            .UseFunctionInvocation(loggerFactory, f => f.MaximumIterationsPerRequest = MaxToolRounds)
            .ConfigureOptions(o =>
            {
                o.AddOllamaOption(OllamaOption.NumCtx, contextTokens);
                o.AddOllamaOption(OllamaOption.MinP, 0f);
                o.Temperature ??= s.Temperature;
                o.TopP ??= s.TopP;
                o.TopK ??= s.TopK;
                o.Seed ??= s.Seed;
                o.MaxOutputTokens ??= s.MaxOutputTokens;
            })
            .Build();
    }
}
