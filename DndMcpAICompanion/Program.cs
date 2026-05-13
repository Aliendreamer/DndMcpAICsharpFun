using System.Threading.RateLimiting;

using DndMcpAICompanion.Features.Auth;
using DndMcpAICompanion.Features.Campaign;
using DndMcpAICompanion.Features.Chat;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
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

var companionDb = builder.Configuration["Data:CompanionDb"] ?? "data/companion.db";
var dbDir = Path.GetDirectoryName(companionDb);
if (!string.IsNullOrEmpty(dbDir)) Directory.CreateDirectory(dbDir);
var connectionString = $"Data Source={companionDb}";

builder.Services.AddSingleton(new UserRepository(connectionString));
builder.Services.AddSingleton(new CampaignRepository(connectionString));
builder.Services.AddSingleton(new HeroRepository(connectionString));
builder.Services.AddSingleton(new ChatRateLimiter(
    builder.Configuration.GetValue("RateLimit:MessagesPerMinute", 10)));

builder.Services.AddTransient<IChatClient>(_ =>
{
    IChatClient inner = new OllamaChatClient(new Uri(ollamaOpts.Url), ollamaOpts.Model);
    return inner.AsBuilder().UseFunctionInvocation().Build();
});

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

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<DndChatService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.LogoutPath = "/logout";
    });
builder.Services.AddAuthorization();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var requestsPerMinute = builder.Configuration.GetValue("RateLimit:RequestsPerMinute", 60);
builder.Services.AddRateLimiter(options =>
{
    options.AddSlidingWindowLimiter("global", o =>
    {
        o.PermitLimit = requestsPerMinute;
        o.Window = TimeSpan.FromMinutes(1);
        o.SegmentsPerWindow = 6;
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        o.QueueLimit = 0;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();

// Initialize companion DB
var userRepo = app.Services.GetRequiredService<UserRepository>();
await userRepo.InitializeAsync();

var campaignRepo = app.Services.GetRequiredService<CampaignRepository>();
await campaignRepo.InitializeAsync();

if (!await userRepo.ExistsAsync("test"))
    await userRepo.CreateAsync("test", PasswordHasher.Hash("test"));

if (string.IsNullOrWhiteSpace(mcpOpts.ApiKey))
    app.Logger.LogWarning("Mcp:ApiKey is not configured — MCP requests will be sent without authentication and will likely be rejected by the server.");

app.Lifetime.ApplicationStopping.Register(() => mcpClient.DisposeAsync().AsTask().GetAwaiter().GetResult());

app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapGet("/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).RequireRateLimiting("global");

app.MapRazorComponents<DndMcpAICompanion.Components.App>()
    .AddInteractiveServerRenderMode()
    .RequireRateLimiting("global");

app.Run();
