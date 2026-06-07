using DndMcpAICsharpFun.Features.Chat;
using Microsoft.Extensions.AI;

namespace DndMcpAICsharpFun.Extensions;

internal static class ChatExtensions
{
    internal static IServiceCollection AddDndChat(this IServiceCollection services, OllamaOptions ollamaOpts)
    {
        services.AddHttpContextAccessor();
        services.AddTransient<IChatClient>(_ =>
        {
            IChatClient inner = new OllamaChatClient(new Uri(ollamaOpts.Url), ollamaOpts.Model);
            return inner.AsBuilder().UseFunctionInvocation().Build();
        });
        services.AddScoped<DndChatService>();
        return services;
    }
}
