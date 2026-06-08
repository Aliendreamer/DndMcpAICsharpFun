using DndMcpAICsharpFun.Extensions;
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Mcp;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.Retrieval.Entities;

using DndMcpAICsharpFun.Infrastructure.Marker;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using DndMcpAICsharpFun.Infrastructure.Ingestion;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) =>
    cfg.ReadFrom.Configuration(ctx.Configuration));

builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 500 * 1024 * 1024);

builder.Configuration
    .AddJsonFile("Config/appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"Config/appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// Options
builder.Services.AddOptions<QdrantOptions>()
    .BindConfiguration("Qdrant")
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<OllamaOptions>()
    .BindConfiguration("Ollama")
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<IngestionOptions>()
    .BindConfiguration("Ingestion")
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<AdminOptions>()
    .BindConfiguration("Admin")
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<RetrievalOptions>()
    .BindConfiguration("Retrieval")
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<MarkerOptions>()
    .BindConfiguration("Marker")
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<DndMcpAICsharpFun.Features.Ingestion.Entities.EntityIngestionOptions>()
    .BindConfiguration("EntityIngestion")
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<McpOptions>()
    .BindConfiguration("Mcp")
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<RerankerOptions>()
    .BindConfiguration("Reranker")
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<CrossEncoderReranker>(sp =>
    new CrossEncoderReranker(
        sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RerankerOptions>>().Value,
        sp.GetRequiredService<ILogger<CrossEncoderReranker>>()));

// builder.Services.AddAntiforgery();
builder.Services.AddInfrastructureClients(builder.Configuration);
builder.Services.AddIngestionPipeline();
builder.Services.AddRetrieval();
builder.Services.AddWebSearch(builder.Configuration);
builder.Services.AddEntityExtraction(builder.Configuration);
builder.Services.AddObservability(builder.Configuration);

// Companion UI: persistence, chat, auth, rate limiting, Blazor, and the loopback MCP client.
builder.Services.AddOptions<DndMcpAICsharpFun.Features.Chat.McpClientOptions>()
    .BindConfiguration("McpClient")
    .ValidateOnStart();
builder.Services.AddOptions<DndMcpAICsharpFun.Infrastructure.Postgres.PostgresOptions>()
    .BindConfiguration("Postgres")
    .ValidateOnStart();
builder.Services.AddAntiforgery();
builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddDndChat(builder.Configuration);
builder.Services.AddDndAuthentication();
builder.Services.AddDndRateLimiting(builder.Configuration);
builder.Services.AddDndBlazor();

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck<QdrantHealthCheck>("qdrant")
    .AddCheck<OllamaHealthCheck>("ollama")
    .AddCheck<MarkerHealthCheck>("marker");

var app = builder.Build();

await app.Services.GetRequiredService<CrossEncoderReranker>().InitializeAsync();

await app.MigrateDatabaseAsync();
await app.InitializeDatabaseAsync();
app.ValidateStartupConfiguration();
app.UseSerilogRequestLogging(o =>
    o.GetLevel = (ctx, _, ex) =>
        ex is not null ? LogEventLevel.Error :
        ctx.Request.Path.StartsWithSegments("/metrics") ? LogEventLevel.Verbose :
        LogEventLevel.Information);

// Companion UI middleware: static files, rate limiter, cookie auth, antiforgery.
app.UseDndMiddleware();
// MCP — guard /mcp with key check, then map the MCP endpoint
app.UseWhen(
    ctx => ctx.Request.Path.StartsWithSegments("/mcp"),
    branch => branch.UseMiddleware<McpAuthMiddleware>());

app.MapAdminMiddleware();
app.MapObservabilityEndpoints();

// Health endpoints
app.MapHealthChecks("/health");
app.MapHealthChecks("/ready");
app.MapHealthChecks("/health/ready");

// Admin routes
var admin = app.MapGroup("/admin");
admin.MapBooksAdmin();
admin.MapFivetoolsAdmin();

// Retrieval endpoints
app.MapRetrievalEndpoints();
app.MapEntityRetrievalEndpoints();
app.MapCanonicalValidationEndpoints();
app.MapCanonicalTypeFixerEndpoints();
app.MapCanonicalNameNormalizerEndpoints();
app.MapMcp("/mcp");

// Blazor UI endpoints (Razor components + logout).
app.MapDndEndpoints();

app.Run();
