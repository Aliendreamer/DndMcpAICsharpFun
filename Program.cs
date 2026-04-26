using DndMcpAICsharpFun.Features.Admin;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.Chunking;
using DndMcpAICsharpFun.Features.Ingestion.Chunking.Detectors;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.VectorStore;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using DndMcpAICsharpFun.Infrastructure.Sqlite;

using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Options;

using OllamaSharp;

using Qdrant.Client;

var builder = WebApplication.CreateBuilder(args);

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

// Infrastructure clients
builder.Services.AddSingleton<QdrantClient>(static sp =>
{
    var opts = sp.GetRequiredService<IOptions<QdrantOptions>>().Value;
    return new QdrantClient(opts.Host, opts.Port, apiKey: opts.ApiKey);
});

builder.Services.AddSingleton<OllamaApiClient>(static sp =>
{
    var opts = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
    return new OllamaApiClient(new Uri(opts.BaseUrl));
});

// SQLite / EF Core
builder.Services.AddDbContext<IngestionDbContext>(static (sp, options) =>
{
    var ingestionOpts = sp.GetRequiredService<IOptions<IngestionOptions>>().Value;
    options.UseSqlite($"Data Source={ingestionOpts.DatabasePath}");
});

// Ingestion tracking
builder.Services.AddScoped<IIngestionTracker, SqliteIngestionTracker>();

// PDF extraction
builder.Services.AddSingleton<IPdfTextExtractor, PdfPigTextExtractor>();

// Chunking
builder.Services.AddSingleton<IPatternDetector, SpellPatternDetector>();
builder.Services.AddSingleton<IPatternDetector, MonsterPatternDetector>();
builder.Services.AddSingleton<IPatternDetector, ClassPatternDetector>();
builder.Services.AddSingleton<IPatternDetector, BackgroundPatternDetector>();
builder.Services.AddSingleton<IPatternDetector, TreasurePatternDetector>();
builder.Services.AddSingleton<IPatternDetector, EncounterPatternDetector>();
builder.Services.AddSingleton<IPatternDetector, TrapPatternDetector>();
builder.Services.AddSingleton<ContentCategoryDetector>();
builder.Services.AddSingleton<DndChunker>();

// Embedding + vector store
builder.Services.AddScoped<IEmbeddingService, OllamaEmbeddingService>();
builder.Services.AddScoped<IVectorStoreService, QdrantVectorStoreService>();
builder.Services.AddScoped<IEmbeddingIngestor, EmbeddingIngestor>();

// Retrieval
builder.Services.AddSingleton<IQdrantSearchClient>(static sp =>
    new QdrantSearchClientAdapter(sp.GetRequiredService<QdrantClient>()));
builder.Services.AddScoped<IRagRetrievalService, RagRetrievalService>();

// Ingestion orchestrator
builder.Services.AddScoped<IIngestionOrchestrator, IngestionOrchestrator>();

// Hosted services — collection initializer must run before ingestion background service
builder.Services.AddHostedService<QdrantCollectionInitializer>();
builder.Services.AddHostedService<IngestionBackgroundService>();

// Health checks
builder.Services.AddHealthChecks()
    .AddCheck<QdrantHealthCheck>("qdrant")
    .AddCheck<OllamaHealthCheck>("ollama");

var app = builder.Build();

// Run EF migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IngestionDbContext>();
    await db.Database.MigrateAsync();
}

// Startup guard: admin key must be configured
var adminOpts = app.Services.GetRequiredService<IOptions<AdminOptions>>().Value;
if (string.IsNullOrWhiteSpace(adminOpts.ApiKey))
    throw new InvalidOperationException("Admin:ApiKey must be configured. Set the Admin__ApiKey environment variable.");

// Admin API key middleware applied only to /admin/* paths
app.UseWhen(
    static ctx => ctx.Request.Path.StartsWithSegments("/admin"),
    static adminApp => adminApp.UseMiddleware<AdminApiKeyMiddleware>()
);

// Health endpoints
app.MapHealthChecks("/health");
app.MapHealthChecks("/ready");
app.MapHealthChecks("/health/ready");


// Admin routes
app.MapGroup("/admin").MapBooksAdmin();

// Retrieval endpoints
app.MapRetrievalEndpoints();

app.Run();
