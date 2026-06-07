using System.Security.Claims;
using DndMcpAICsharpFun.Domain;
using Microsoft.Extensions.AI;

namespace DndMcpAICsharpFun.Features.Chat;

public sealed class DndChatService(
    IChatClient chatClient,
    McpToolsProvider toolsProvider,
    ChatRepository chatRepository,
    IHttpContextAccessor httpContextAccessor,
    ChatRateLimiter rateLimiter)
{
    public List<ChatMessage> History { get; } = [];

    public async Task<string> SendAsync(string userMessage, bool allowWebSearch, CancellationToken ct)
    {
        var ip = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!rateLimiter.TryAcquire(ip))
        {
            const string limited = "You're sending messages too quickly. Please wait a moment.";
            History.Add(new ChatMessage(ChatRole.User, userMessage));
            History.Add(new ChatMessage(ChatRole.Assistant, limited));
            return limited;
        }

        var tools = await toolsProvider.GetToolsAsync(ct);
        var activeTools = allowWebSearch
            ? tools
            : tools.Where(t => t is not AIFunction fn || fn.Name != "search_web").ToList();

        History.Add(new ChatMessage(ChatRole.User, userMessage));
        await PersistAsync("user", userMessage);
        try
        {
            var response = await chatClient.GetResponseAsync(
                History,
                new ChatOptions { Tools = [.. activeTools] },
                ct);
            var reply = response.Text ?? string.Empty;
            History.Add(new ChatMessage(ChatRole.Assistant, reply));
            await PersistAsync("assistant", reply);
            return reply;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            const string error = "The AI is unavailable right now. Please try again.";
            History.Add(new ChatMessage(ChatRole.Assistant, error));
            return error;
        }
    }

    private async Task PersistAsync(string role, string content)
    {
        try
        {
            var idClaim = httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!long.TryParse(idClaim, out var userId)) return;
            await chatRepository.AddAsync(new ChatTurn
            {
                UserId = userId,
                Role = role,
                Content = content,
                CreatedAt = DateTime.UtcNow,
            });
        }
        catch
        {
            // Persistence of chat history must never break the chat response.
        }
    }
}
