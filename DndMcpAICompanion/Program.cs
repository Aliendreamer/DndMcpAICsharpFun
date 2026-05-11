using DndMcpAICompanion.Features.Chat;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

// Alias to avoid ambiguity with ModelContextProtocol.Client.McpClientOptions
using AppMcpClientOptions = DndMcpAICompanion.Features.Chat.McpClientOptions;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("Config/appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"Config/appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

var ollamaOpts = builder.Configuration.GetSection("Ollama").Get<OllamaOptions>()
    ?? throw new InvalidOperationException("Ollama configuration is missing.");
var mcpOpts = builder.Configuration.GetSection("Mcp").Get<AppMcpClientOptions>()
    ?? throw new InvalidOperationException("Mcp configuration is missing.");

builder.Services.AddTransient<IChatClient>(
    _ => new OllamaChatClient(new Uri(ollamaOpts.Url), ollamaOpts.Model));

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
var mcpTools = await mcpClient.ListToolsAsync();
builder.Services.AddSingleton(mcpClient);
builder.Services.AddSingleton<IReadOnlyList<AITool>>(mcpTools.Cast<AITool>().ToList());

builder.Services.AddScoped<DndChatService>();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (string.IsNullOrWhiteSpace(mcpOpts.ApiKey))
    app.Logger.LogWarning("Mcp:ApiKey is not configured — MCP requests will be sent without authentication and will likely be rejected by the server.");

app.Lifetime.ApplicationStopping.Register(() => mcpClient.DisposeAsync().AsTask().GetAwaiter().GetResult());

app.UseStaticFiles();
app.UseAntiforgery();
app.MapRazorComponents<DndMcpAICompanion.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
