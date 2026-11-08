namespace DndMcpAICsharpFun.Infrastructure.Http;

/// <summary>
/// Builds an HttpClient for calls that legitimately run longer than the .NET default 100 s timeout:
/// a qwen3 chat turn with tool calls (think-on, multi-hop retrieval, the level-up coach) and the chat's
/// loopback MCP calls whose tools themselves drive the model. The default cut those turns off mid-answer.
/// </summary>
public static class LongRunningHttpClient
{
    public static HttpClient Create(Uri? baseAddress, int timeoutSeconds)
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
        if (baseAddress is not null)
            client.BaseAddress = baseAddress;
        return client;
    }
}
