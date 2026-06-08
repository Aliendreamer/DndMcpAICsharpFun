using System.Security.Claims;
using DndMcpAICsharpFun.Domain;
using Microsoft.Extensions.AI;

namespace DndMcpAICsharpFun.Features.Chat;

public sealed class DndChatService(
    IChatClient chatClient,
    IMcpToolsProvider toolsProvider,
    ChatRepository chatRepository,
    IHttpContextAccessor httpContextAccessor,
    ChatRateLimiter rateLimiter)
{
    public List<ChatMessage> History { get; } = [];

    /// <summary>
    /// Populate <see cref="History"/> from the signed-in user's persisted chat turns,
    /// so the conversation is replayed when the page is (re)opened. No-op if already loaded
    /// or if there is no authenticated user. Caps replay to the most recent turns to bound
    /// the LLM context window.
    /// </summary>
    public async Task LoadHistoryAsync(int maxTurns = 40)
    {
        if (History.Count > 0) return;
        var idClaim = httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!long.TryParse(idClaim, out var userId)) return;

        var turns = await chatRepository.GetHistoryAsync(userId);
        foreach (var t in turns.TakeLast(maxTurns))
            History.Add(new ChatMessage(
                t.Role == "user" ? ChatRole.User : ChatRole.Assistant, t.Content));
    }

    public async Task<bool> SendAsync(string userMessage, bool allowWebSearch, CancellationToken ct)
    {
        var ip = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!rateLimiter.TryAcquire(ip))
        {
            const string limited = "You're sending messages too quickly. Please wait a moment.";
            History.Add(new ChatMessage(ChatRole.User, userMessage));
            History.Add(new ChatMessage(ChatRole.Assistant, limited));
            return true;
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
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Failure is surfaced to the caller (UI banner); no assistant bubble is injected.
            return false;
        }
    }

    /// <summary>
    /// Clears the in-memory conversation and permanently deletes the signed-in user's
    /// persisted chat turns so the conversation does not replay on reload.
    /// </summary>
    public async Task ClearAsync()
    {
        History.Clear();
        var idClaim = httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (long.TryParse(idClaim, out var userId))
            await chatRepository.DeleteConversationAsync(userId);
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
