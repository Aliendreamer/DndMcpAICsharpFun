using System.Diagnostics.CodeAnalysis;

using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.VectorStore;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using DndMcpAICsharpFun.Infrastructure;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using DndMcpAICsharpFun.Infrastructure.Sqlite;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Microsoft.Extensions.AI;

using OllamaSharp;

using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

using Qdrant.Client;

namespace DndMcpAICsharpFun.Extensions;

[ExcludeFromCodeCoverage]
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
        services.AddSingleton<IPdfStructuredExtractor, PdfPigStructuredExtractor>();

        services.AddScoped<IEmbeddingService, OllamaEmbeddingService>();
        services.AddScoped<IVectorStoreService, QdrantVectorStoreService>();
        services.AddScoped<IEntityVectorStore, QdrantEntityVectorStore>();

        services.AddScoped<IBlockIngestionOrchestrator, BlockIngestionOrchestrator>();
        services.AddScoped<IBookDeletionService, BookDeletionService>();

        services.AddSingleton<CanonicalJsonLoader>();
        services.AddSingleton<EntityCanonicalTextDispatcher>();
        services.AddSingleton<EntityReferenceResolver>();
        services.AddScoped<IEntityIngestionOrchestrator, EntityIngestionOrchestrator>();
        services.AddScoped<DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.FivetoolsIngestionService>();

        services.AddHostedService<QdrantCollectionInitializer>();

        services.AddSingleton<IngestionQueueWorker>();
        services.AddSingleton<IIngestionQueue>(sp => sp.GetRequiredService<IngestionQueueWorker>());
        services.AddHostedService(sp => sp.GetRequiredService<IngestionQueueWorker>());

        services.AddSingleton<IPdfBookmarkReader, PdfPigBookmarkReader>();

        services.AddSingleton<DoclingPdfConverter>();
        services.AddSingleton<IDoclingPdfConverter>(sp => new DoclingDiskCache(
            sp.GetRequiredService<DoclingPdfConverter>(),
            sp.GetRequiredService<IOptions<EntityExtractionOptions>>(),
            sp.GetRequiredService<ILogger<DoclingDiskCache>>()));
        services.AddSingleton<IPdfBlockExtractor, DoclingBlockExtractor>();

        return services;
    }

    internal static IServiceCollection AddRetrieval(this IServiceCollection services)
    {
        services.AddSingleton<IQdrantSearchClient>(static sp =>
            new QdrantSearchClientAdapter(sp.GetRequiredService<QdrantClient>()));
        services.AddScoped<IRagRetrievalService, RagRetrievalService>();
        services.AddScoped<DndMcpAICsharpFun.Features.Retrieval.Entities.IEntityRetrievalService, DndMcpAICsharpFun.Features.Retrieval.Entities.EntityRetrievalService>();

        return services;
    }

    internal static IServiceCollection AddEntityExtraction(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EntityExtractionOptions>(configuration.GetSection("EntityExtraction"));
        services.AddSingleton<IChatClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
            return new OllamaChatClient(new Uri(opts.BaseUrl), opts.ChatModel);
        });
        services.AddSingleton<IEntityExtractionLlmClient, OllamaEntityExtractionClient>();
        services.AddSingleton<ExtractionPromptBuilder>();
        services.AddSingleton<EntityCandidateScanner>();
        services.AddSingleton<CanonicalJsonWriter>();
        services.AddSingleton<ExtractionErrorsFile>();
        services.AddSingleton<ExtractionWarningsFile>();
        services.AddSingleton<ExtractionRetryPolicy>();
        services.AddScoped<IEntityExtractionOrchestrator, EntityExtractionOrchestrator>();
        services.AddSingleton<DndMcpAICsharpFun.Features.Admin.CanonicalValidationService>();
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
