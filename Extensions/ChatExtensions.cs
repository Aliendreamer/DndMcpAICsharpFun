using DndMcpAICsharpFun.Features.Chat;
using Microsoft.Extensions.AI;

namespace DndMcpAICsharpFun.Extensions;

internal static class ChatExtensions
{
    internal static IServiceCollection AddDndChat(this IServiceCollection services, IConfiguration config)
    {
        var baseUrl = config["Ollama:BaseUrl"] ?? "http://ollama:11434";
        var chatModel = config["Ollama:ChatModel"] ?? "qwen3:8b";
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
