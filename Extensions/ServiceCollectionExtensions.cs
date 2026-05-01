using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.Chunking;
using DndMcpAICsharpFun.Features.Ingestion.Chunking.Detectors;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.VectorStore;
using DndMcpAICsharpFun.Infrastructure;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using DndMcpAICsharpFun.Infrastructure.Sqlite;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using OllamaSharp;

using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

using Qdrant.Client;

namespace DndMcpAICsharpFun.Extensions;

internal static class ServiceCollectionExtensions
{
    internal static IServiceCollection AddInfrastructureClients(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(static sp =>
        {
            var opts = sp.GetRequiredService<IOptions<QdrantOptions>>().Value;
            return new QdrantClient(opts.Host, opts.Port);
        });

        services.AddSingleton(static sp =>
        {
            var opts = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
            // Timeout.InfiniteTimeSpan: model cold-start (loading from disk) can take
            // several minutes; the default 100 s HttpClient timeout cuts it off prematurely.
            // CancellationToken passed to each call controls per-request cancellation instead.
            var httpClient = new HttpClient
            {
                BaseAddress = new Uri(opts.BaseUrl),
                Timeout = Timeout.InfiniteTimeSpan
            };
            return new OllamaApiClient(httpClient);
        });

        services.AddSingleton<IOllamaApiClient>(sp => sp.GetRequiredService<OllamaApiClient>());

        services.AddDbContext<IngestionDbContext>(static (sp, options) =>
        {
            var ingestionOpts = sp.GetRequiredService<IOptions<IngestionOptions>>().Value;
            options.UseSqlite($"Data Source={ingestionOpts.DatabasePath}");
        });

        return services;
    }

    internal static IServiceCollection AddIngestionPipeline(this IServiceCollection services)
    {
        services.AddScoped<IIngestionTracker, SqliteIngestionTracker>();
        services.AddSingleton<IPdfTextExtractor, PdfPigTextExtractor>();

        services.AddSingleton<IPatternDetector, SpellPatternDetector>();
        services.AddSingleton<IPatternDetector, MonsterPatternDetector>();
        services.AddSingleton<IPatternDetector, ClassPatternDetector>();
        services.AddSingleton<IPatternDetector, BackgroundPatternDetector>();
        services.AddSingleton<IPatternDetector, TreasurePatternDetector>();
        services.AddSingleton<IPatternDetector, EncounterPatternDetector>();
        services.AddSingleton<IPatternDetector, TrapPatternDetector>();
        services.AddSingleton<ContentCategoryDetector>();
        services.AddSingleton<DndChunker>();

        services.AddScoped<IEmbeddingService, OllamaEmbeddingService>();
        services.AddScoped<IVectorStoreService, QdrantVectorStoreService>();
        services.AddScoped<IEmbeddingIngestor, EmbeddingIngestor>();

        services.AddScoped<IIngestionOrchestrator, IngestionOrchestrator>();

        services.AddScoped<ILlmClassifier, OllamaLlmClassifier>();
        services.AddScoped<ILlmEntityExtractor, OllamaLlmEntityExtractor>();
        services.AddSingleton<IEntityJsonStore, EntityJsonStore>();
        services.AddScoped<IJsonIngestionPipeline, JsonIngestionPipeline>();

        services.AddHostedService<QdrantCollectionInitializer>();

        services.AddSingleton<IngestionQueueWorker>();
        services.AddSingleton<IIngestionQueue>(sp => sp.GetRequiredService<IngestionQueueWorker>());
        services.AddHostedService(sp => sp.GetRequiredService<IngestionQueueWorker>());

        services.AddSingleton<IPdfBookmarkReader, PdfPigBookmarkReader>();
        services.AddSingleton<ITocCategoryClassifier, OllamaTocCategoryClassifier>();
        services.AddSingleton<IExtractionCancellationRegistry, ExtractionCancellationRegistry>();

        return services;
    }

    internal static IServiceCollection AddRetrieval(this IServiceCollection services)
    {
        services.AddSingleton<IQdrantSearchClient>(static sp =>
            new QdrantSearchClientAdapter(sp.GetRequiredService<QdrantClient>()));
        services.AddScoped<IRagRetrievalService, RagRetrievalService>();

        return services;
    }

    internal static IServiceCollection AddObservability(this IServiceCollection services, IConfiguration configuration)
    {
        var otelOptions = configuration.GetSection("OpenTelemetry").Get<OpenTelemetryOptions>() ?? new OpenTelemetryOptions();
        if (!otelOptions.Enabled)
            return services;

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(otelOptions.ServiceName))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddPrometheusExporter());

        return services;
    }
}
