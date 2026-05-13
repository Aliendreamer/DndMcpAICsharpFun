using DndMcpAICompanion.Extensions;
using DndMcpAICompanion.Features.Chat;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using AppMcpClientOptions = DndMcpAICompanion.Features.Chat.McpClientOptions;

var builder = WebApplication.CreateBuilder(args);

builder.AddDndConfiguration();

var ollamaOpts = builder.Configuration.GetSection("Ollama").Get<OllamaOptions>()
    ?? throw new InvalidOperationException("Ollama configuration is missing.");
var mcpOpts = builder.Configuration.GetSection("Mcp").Get<AppMcpClientOptions>()
    ?? throw new InvalidOperationException("Mcp configuration is missing.");

var transport = new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = new Uri(mcpOpts.Url),
    TransportMode = HttpTransportMode.StreamableHttp,
    AdditionalHeaders = new Dictionary<string, string>
    {
        ["X-Mcp-Api-Key"] = mcpOpts.ApiKey
    }
});
var mcpClient = await McpClient.CreateAsync(transport);
IReadOnlyList<AITool> mcpTools = (await mcpClient.ListToolsAsync()).Cast<AITool>().ToList();

builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddDndChat(ollamaOpts);
builder.Services.AddMcpClient(mcpClient, mcpTools);
builder.Services.AddDndAuthentication();
builder.Services.AddDndRateLimiting(builder.Configuration);
builder.Services.AddDndBlazor();

var app = builder.Build();

await app.InitializeDatabaseAsync();
app.UseDndMiddleware();
app.MapDndEndpoints(mcpOpts, mcpClient);

app.Run();
