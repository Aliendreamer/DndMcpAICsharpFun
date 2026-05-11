using Microsoft.Extensions.AI;

namespace DndMcpAICompanion.Features.Chat;

public sealed class DndChatService(IChatClient chatClient, IReadOnlyList<AITool> tools)
{
    public List<ChatMessage> History { get; } = [];

    public async Task<string> SendAsync(string userMessage, CancellationToken ct)
    {
        History.Add(new ChatMessage(ChatRole.User, userMessage));
        try
        {
            var response = await chatClient.GetResponseAsync(
                History,
                new ChatOptions { Tools = [.. tools] },
                ct);
            var reply = response.Text ?? string.Empty;
            History.Add(new ChatMessage(ChatRole.Assistant, reply));
            return reply;
        }
        catch (Exception)
        {
            const string error = "The AI is unavailable right now. Please try again.";
            History.Add(new ChatMessage(ChatRole.Assistant, error));
            return error;
        }
    }
}
