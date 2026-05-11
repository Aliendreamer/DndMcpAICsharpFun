using Microsoft.Extensions.AI;

namespace DndMcpAICompanion.Features.Chat;

public sealed class DndChatService(
    IChatClient chatClient,
    IReadOnlyList<AITool> tools,
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

        var activeTools = allowWebSearch
            ? tools
            : tools.Where(t => t is not AIFunction fn || fn.Name != "search_web").ToList();

        History.Add(new ChatMessage(ChatRole.User, userMessage));
        try
        {
            var response = await chatClient.GetResponseAsync(
                History,
                new ChatOptions { Tools = [.. activeTools] },
                ct);
            var reply = response.Text ?? string.Empty;
            History.Add(new ChatMessage(ChatRole.Assistant, reply));
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
}
