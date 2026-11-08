using DndMcpAICsharpFun.Features.CharacterAdvice;
using DndMcpAICsharpFun.Features.Chat;
using DndMcpAICsharpFun.Features.Downtime;
using DndMcpAICsharpFun.Features.Lore;
using DndMcpAICsharpFun.Features.Npc;
using DndMcpAICsharpFun.Features.Rules;
using DndMcpAICsharpFun.Features.SessionPrep;

using Microsoft.Extensions.AI;

using OllamaSharp;

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
        services.AddTransient<IChatClient>(_ =>
        {
            // OllamaSharp's OllamaApiClient (NOT MEAI.Ollama's OllamaChatClient) — only it can send
            // the top-level `think` field that DndChatService uses to run qwen3 think-off. It
            // implements MEAI IChatClient, so the FunctionInvocation stack wraps it identically.
            // Explicit HttpClient: the Uri constructor gets the .NET default 100 s timeout, which cut
            // long tool-calling turns (the level-up coach) off mid-answer.
            var chatHttp = Infrastructure.Http.LongRunningHttpClient.Create(
                new Uri(baseUrl), config.GetValue("Ollama:ChatTimeoutSeconds", 600));
            IChatClient inner = new OllamaApiClient(chatHttp, chatModel);
            return inner.AsBuilder().UseFunctionInvocation().Build();
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
}