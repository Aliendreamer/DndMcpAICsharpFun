using DndMcpAICsharpFun.Extensions;
using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
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
builder.Services.Configure<QdrantOptions>(builder.Configuration.GetSection("Qdrant"));
builder.Services.Configure<OllamaOptions>(builder.Configuration.GetSection("Ollama"));
builder.Services.Configure<IngestionOptions>(builder.Configuration.GetSection("Ingestion"));
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection("Admin"));
builder.Services.Configure<RetrievalOptions>(builder.Configuration.GetSection("Retrieval"));

// builder.Services.AddAntiforgery();
builder.Services.AddInfrastructureClients(builder.Configuration);
builder.Services.AddIngestionPipeline();
builder.Services.AddRetrieval();
builder.Services.AddObservability(builder.Configuration);

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck<QdrantHealthCheck>("qdrant")
    .AddCheck<OllamaHealthCheck>("ollama");

var app = builder.Build();

await app.MigrateDatabaseAsync();
app.ValidateStartupConfiguration();
// app.UseAntiforgery();
app.UseSerilogRequestLogging(o =>
    o.GetLevel = (ctx, _, ex) =>
        ex is not null ? LogEventLevel.Error :
        ctx.Request.Path.StartsWithSegments("/metrics") ? LogEventLevel.Verbose :
        LogEventLevel.Information);
app.MapAdminMiddleware();
app.MapObservabilityEndpoints();

// Health endpoints
app.MapHealthChecks("/health");
app.MapHealthChecks("/ready");
app.MapHealthChecks("/health/ready");

// Admin routes
app.MapGroup("/admin").MapBooksAdmin();

// Retrieval endpoints
app.MapRetrievalEndpoints();

app.Run();
