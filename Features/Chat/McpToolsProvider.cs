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
/// A failed init is NOT cached forever — it self-heals: a transient failure records the
/// failure time and short-circuits to an empty tool set for <see cref="RetryCooldown"/>,
/// after which the next call retries the connection instead of leaving chat permanently
/// tool-less for the rest of the process lifetime.
/// </summary>
public sealed class McpToolsProvider(
    IOptions<AppMcpClientOptions> options,
    ILogger<McpToolsProvider> logger,
    TimeProvider? timeProvider = null,
    Func<CancellationToken, Task<(IAsyncDisposable? Client, IReadOnlyList<AITool> Tools)>>? connect = null)
    : IMcpToolsProvider, IAsyncDisposable
{
    internal static readonly TimeSpan RetryCooldown = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly Func<CancellationToken, Task<(IAsyncDisposable? Client, IReadOnlyList<AITool> Tools)>> _connect =
        connect ?? (ct => ConnectViaHttpAsync(options.Value, ct));
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IAsyncDisposable? _client;
    private IReadOnlyList<AITool>? _tools;
    private DateTimeOffset? _lastFailureAt;

    public async Task<IReadOnlyList<AITool>> GetToolsAsync(CancellationToken ct = default)
    {
        if (_tools is not null) return _tools;
        await _gate.WaitAsync(ct);
        try
        {
            if (_tools is not null) return _tools;

            if (_lastFailureAt is { } lastFailure && _clock.GetUtcNow() - lastFailure < RetryCooldown)
                return [];

            var (client, tools) = await _connect(ct);
            _client = client;
            _tools = tools;
            _lastFailureAt = null;
            logger.LogInformation("MCP client initialised with {Count} tools.", _tools.Count);
            return _tools;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialise the MCP client; chat will proceed without tools.");
            _lastFailureAt = _clock.GetUtcNow();
            return [];
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<(IAsyncDisposable?, IReadOnlyList<AITool>)> ConnectViaHttpAsync(
        AppMcpClientOptions opts, CancellationToken ct)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(opts.Url),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["X-Mcp-Api-Key"] = opts.ApiKey },
        });
        var client = await McpClient.CreateAsync(transport, cancellationToken: ct);
        var tools = (await client.ListToolsAsync(cancellationToken: ct)).Cast<AITool>().ToList();
        return (client, tools);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null) await _client.DisposeAsync();
        _gate.Dispose();
    }
}