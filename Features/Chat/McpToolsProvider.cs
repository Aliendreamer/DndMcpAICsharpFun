using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using AppMcpClientOptions = DndMcpAICsharpFun.Features.Chat.McpClientOptions;

namespace DndMcpAICsharpFun.Features.Chat;

/// <summary>Supplies the MCP tool set to the chat service.</summary>
public interface IMcpToolsProvider
{
    Task<IReadOnlyList<AITool>> GetToolsAsync(CancellationToken ct = default);
}

/// <summary>
/// Lazily creates the MCP client and lists its tools on first use. In the merged
/// single-process host the MCP server and client share a process, so creating the
/// client eagerly at startup would self-deadlock (the server is not yet listening).
/// </summary>
public sealed class McpToolsProvider(
    IOptions<AppMcpClientOptions> options,
    ILogger<McpToolsProvider> logger) : IMcpToolsProvider, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private McpClient? _client;
    private IReadOnlyList<AITool>? _tools;

    public async Task<IReadOnlyList<AITool>> GetToolsAsync(CancellationToken ct = default)
    {
        if (_tools is not null) return _tools;
        await _gate.WaitAsync(ct);
        try
        {
            if (_tools is not null) return _tools;
            var opts = options.Value;
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(opts.Url),
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string> { ["X-Mcp-Api-Key"] = opts.ApiKey },
            });
            _client = await McpClient.CreateAsync(transport, cancellationToken: ct);
            _tools = (await _client.ListToolsAsync(cancellationToken: ct)).Cast<AITool>().ToList();
            logger.LogInformation("MCP client initialised with {Count} tools.", _tools.Count);
            return _tools;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialise the MCP client; chat will proceed without tools.");
            _tools = [];
            return _tools;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null) await _client.DisposeAsync();
        _gate.Dispose();
    }
}
