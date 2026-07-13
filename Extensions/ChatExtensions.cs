using DndMcpAICsharpFun.Features.CharacterAdvice;
using DndMcpAICsharpFun.Features.Chat;
using DndMcpAICsharpFun.Features.Lore;
using DndMcpAICsharpFun.Features.Npc;
using DndMcpAICsharpFun.Features.Rules;
using DndMcpAICsharpFun.Features.SessionPrep;

using Microsoft.Extensions.AI;

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
            IChatClient inner = new OllamaChatClient(new Uri(baseUrl), chatModel);
            return inner.AsBuilder().UseFunctionInvocation().Build();
        });
        services.AddScoped<DndChatService>();
        return services;
    }
}