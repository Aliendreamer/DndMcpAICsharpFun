using DndMcpAICsharpFun.Extensions;
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Mcp;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.Retrieval.Entities;

using DndMcpAICsharpFun.Infrastructure.Ollama;
using DndMcpAICsharpFun.Infrastructure.Qdrant;

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
// Cross-cutting host options (no owning feature extension): admin-api-key + MCP-server auth.
builder.Services.AddOptions<AdminOptions>()
    .BindConfiguration("Admin")
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<McpOptions>()
    .BindConfiguration("Mcp")
    .ValidateDataAnnotations()
    .ValidateOnStart();

// builder.Services.AddAntiforgery();
builder.Services.AddInfrastructureClients(builder.Configuration);
builder.Services.AddIngestionPipeline();
builder.Services.AddRetrieval();
builder.Services.AddEncounters();
builder.Services.AddWebSearch(builder.Configuration);
builder.Services.AddEntityExtraction(builder.Configuration);
builder.Services.AddObservability(builder.Configuration);

// Companion UI: persistence, chat, auth, rate limiting, Blazor, and the loopback MCP client.
builder.Services.AddAntiforgery();
builder.Services.AddDatabase(builder.Configuration);
builder.Services.AddDndChat(builder.Configuration);
builder.Services.AddDndAuthentication();
builder.Services.AddDndRateLimiting(builder.Configuration);
// Behind a TLS-terminating reverse proxy: honour forwarded headers so the real client IP and the
// HTTPS scheme are used by rate limiting, auth cookies, and logging. Trusted proxies come from
// ForwardedHeaders:KnownProxies (empty by default → the platform's loopback proxy).
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
        | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
    foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
        if (System.Net.IPAddress.TryParse(proxy, out var ip))
            o.KnownProxies.Add(ip);
});
builder.Services.AddDndBlazor();

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck<QdrantHealthCheck>("qdrant")
    .AddCheck<OllamaHealthCheck>("ollama");

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

// Forwarded headers must run first so downstream middleware sees the real client IP/scheme.
app.UseForwardedHeaders();
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
admin.MapNeedsReview();
admin.MapCanonicalValidation();
admin.MapRetrievalAdmin();

// Retrieval endpoints — anonymous but rate-limited per client (SEC-10).
app.MapRetrievalEndpoints();
app.MapEntityRetrievalEndpoints();
app.MapCanonicalTypeFixerEndpoints();
app.MapCanonicalNameNormalizerEndpoints();
app.MapMcp("/mcp");

// Static web assets (app.css, app.js, _framework/blazor.web.js) served from the publish
// endpoints manifest — required because the .NET SDK no longer copies them into a physical
// wwwroot on publish. Must precede the Razor component endpoints.
app.MapStaticAssets();

// Blazor UI endpoints (Razor components + logout).
app.MapDndEndpoints();

app.Run();