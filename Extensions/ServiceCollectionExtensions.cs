using System.Diagnostics.CodeAnalysis;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Embedding;
using DndMcpAICsharpFun.Features.Entities;
using DndMcpAICsharpFun.Features.Entities.CanonicalText;
using DndMcpAICsharpFun.Features.Ingestion;
using DndMcpAICsharpFun.Features.Ingestion.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion;
using DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.Providers;
using DndMcpAICsharpFun.Features.Ingestion.Tracking;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.Search;
using DndMcpAICsharpFun.Features.VectorStore;
using DndMcpAICsharpFun.Features.VectorStore.Entities;
using DndMcpAICsharpFun.Infrastructure;
using DndMcpAICsharpFun.Infrastructure.Ollama;
using DndMcpAICsharpFun.Infrastructure.Qdrant;
using DndMcpAICsharpFun.Infrastructure.Ingestion;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Microsoft.Extensions.AI;

using OllamaSharp;

using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;

using Qdrant.Client;

using DndMcpAICsharpFun.Infrastructure.Persistence;
using DndMcpAICsharpFun.Infrastructure.Postgres;

namespace DndMcpAICsharpFun.Extensions;

[ExcludeFromCodeCoverage]
internal static class ServiceCollectionExtensions
{
    internal static IServiceCollection AddInfrastructureClients(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<QdrantOptions>()
            .BindConfiguration("Qdrant")
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<OllamaOptions>()
            .BindConfiguration("Ollama")
            .ValidateDataAnnotations()
            .ValidateOnStart();

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

        services.AddDbContextFactory<AppDbContext>(static (sp, options) =>
        {
            var pg = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;
            options.UseNpgsql(pg.ConnectionString(), o => o.EnableRetryOnFailure());
        });
        // Scoped AppDbContext (for the ingestion tracker + startup migration) delegates to the factory.
        services.AddScoped<AppDbContext>(sp =>
            sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

        return services;
    }

    internal static IServiceCollection AddIngestionPipeline(this IServiceCollection services)
    {
        services.AddOptions<IngestionOptions>()
            .BindConfiguration("Ingestion")
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddOptions<DndMcpAICsharpFun.Features.Ingestion.Entities.EntityIngestionOptions>()
            .BindConfiguration("EntityIngestion")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<IIngestionTracker, IngestionTracker>();
        services.AddScoped<DndMcpAICsharpFun.Features.Retrieval.IBm25CorpusStats,
            DndMcpAICsharpFun.Features.Retrieval.Bm25CorpusStatsStore>();
        services.AddScoped<IEmbeddingService, OllamaEmbeddingService>();
        services.AddScoped<IVectorStoreService, QdrantVectorStoreService>();
        services.AddScoped<IEntityVectorStore, QdrantEntityVectorStore>();

        services.AddScoped<IBlockIngestionOrchestrator, BlockIngestionOrchestrator>();
        services.AddScoped<IBookDeletionService, BookDeletionService>();

        services.AddSingleton<CanonicalJsonLoader>();
        services.AddSingleton<DndMcpAICsharpFun.Features.Resolution.StructuredFactProjector>();
        services.AddSingleton<DndMcpAICsharpFun.Features.Resolution.CharacterResolutionService>();
        services.AddSingleton<DndMcpAICsharpFun.Features.Resolution.MulticlassSlotTableSeeder>();
        services.AddSingleton<EntityCanonicalTextDispatcher>();
        services.AddSingleton<EntityReferenceResolver>();
        services.AddScoped<IEntityIngestionOrchestrator, EntityIngestionOrchestrator>();
        services.AddScoped<DndMcpAICsharpFun.Features.Ingestion.FivetoolsIngestion.FivetoolsIngestionService>();

        services.AddSingleton<QdrantSparseState>();
        services.AddHostedService<QdrantCollectionInitializer>();

        services.AddSingleton<IngestionQueueWorker>();
        services.AddSingleton<IIngestionQueue>(sp => sp.GetRequiredService<IngestionQueueWorker>());
        services.AddHostedService(sp => sp.GetRequiredService<IngestionQueueWorker>());

        services.AddSingleton<BookSourceRegistry>();
        services.AddSingleton<IPdfBookmarkReader, PdfPigBookmarkReader>();

        // MinerU is the sole PDF structure converter, called over HTTP against the mineru:8000
        // service. The parser-agnostic PdfConversionDiskCache wraps it to memoise conversions.
        services.AddOptions<MinerUOptions>().BindConfiguration("MinerU");
        services.AddHttpClient(nameof(MinerUPdfConverter))
            .ConfigureHttpClient((sp, c) =>
                c.Timeout = TimeSpan.FromMinutes(
                    sp.GetRequiredService<IOptions<MinerUOptions>>().Value.ConversionTimeoutMinutes));
        services.AddSingleton<MinerUPdfConverter>();
        services.AddSingleton<IPdfStructureConverter>(sp =>
            new PdfConversionDiskCache(
                sp.GetRequiredService<MinerUPdfConverter>(),
                sp.GetRequiredService<IOptions<EntityExtractionOptions>>(),
                sp.GetRequiredService<ILogger<PdfConversionDiskCache>>()));
        services.AddSingleton<IPdfBlockExtractor, StructureBlockExtractor>();

        return services;
    }

    internal static IServiceCollection AddRetrieval(this IServiceCollection services)
    {
        services.AddOptions<RetrievalOptions>()
            .BindConfiguration("Retrieval")
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IQdrantSearchClient>(static sp =>
            new QdrantSearchClientAdapter(
                sp.GetRequiredService<QdrantClient>(),
                sp.GetRequiredService<IOptions<QdrantOptions>>()));
        services.AddSingleton<DndMcpAICsharpFun.Features.Retrieval.RerankingService>();
        services.AddScoped<DndMcpAICsharpFun.Features.Retrieval.IFusedRetrievalService, DndMcpAICsharpFun.Features.Retrieval.FusedRetrievalService>();
        services.AddScoped<IRagRetrievalService, RagRetrievalService>();
        services.AddScoped<DndMcpAICsharpFun.Features.Retrieval.Entities.IEntityRetrievalService, DndMcpAICsharpFun.Features.Retrieval.Entities.EntityRetrievalService>();
        services.AddScoped<DndMcpAICsharpFun.Features.Retrieval.Entities.Dedup.IBookTypeLookup, DndMcpAICsharpFun.Features.Retrieval.Entities.Dedup.BookTypeLookup>();
        services.AddScoped<DndMcpAICsharpFun.Features.Retrieval.Entities.Dedup.EntityDuplicateService>();

        services.AddOptions<RerankerOptions>()
            .BindConfiguration("Reranker")
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddHttpClient("reranker", static c => c.Timeout = TimeSpan.FromMinutes(10));
        services.AddSingleton<CrossEncoderReranker>(static sp => new CrossEncoderReranker(
            sp.GetRequiredService<IOptions<RerankerOptions>>().Value,
            sp.GetRequiredService<IHttpClientFactory>(),
            sp.GetRequiredService<ILogger<CrossEncoderReranker>>()));
        services.AddSingleton<IReranker>(static sp => sp.GetRequiredService<CrossEncoderReranker>());

        return services;
    }

    internal static IServiceCollection AddWebSearch(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SearXNGOptions>()
            .BindConfiguration("SearXNG")
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddHttpClient<SearXNGClient>((sp, client) =>
        {
            var opts = sp.GetRequiredService<IOptions<SearXNGOptions>>().Value;
            client.BaseAddress = new Uri(opts.Url);
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        return services;
    }

    internal static IServiceCollection AddEntityExtraction(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EntityExtractionOptions>()
            .BindConfiguration("EntityExtraction")
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.Configure<GroundingOptions>(configuration.GetSection("Grounding"));
        services.AddScoped<ITier1Grounding, Tier1EmbeddingGrounding>();
        services.AddSingleton<IChatClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
            return new OllamaChatClient(new Uri(opts.BaseUrl), opts.ChatModel);
        });
        services.AddSingleton<IEntityExtractionLlmClient, OllamaEntityExtractionClient>();
        services.AddScoped<IGroundingJudge, QwenGroundingJudge>();
        services.AddScoped<GroundingCascade>();
        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<EntityExtractionOptions>>().Value;
            return new ExtractionPromptBuilder(ExtractionPromptBuilder.LoadExamples(opts.ExamplesDirectory));
        });
        services.AddSingleton<PartialJsonRecoverer>();
        services.AddSingleton<SemanticChunker>();
        services.AddSingleton<EntityFieldMerger>();
        services.AddSingleton<EntityCandidateScanner>();
        services.AddSingleton<StatBlockScanner>();
        services.AddSingleton<CanonicalJsonWriter>();
        services.AddSingleton<ExtractionErrorsFile>();
        services.AddSingleton<ExtractionWarningsFile>();
        services.AddSingleton<ExtractionDeclinedFile>();
        services.AddSingleton<ExtractionRetryPolicy>();
        services.AddSingleton<EntitySchemaProvider>();
        services.AddSingleton<ExtractionCheckpointStore>();
        services.AddSingleton<CandidateExtractor>();
        services.AddSingleton<EntityNameIndex>(_ =>
            new EntityNameIndex(
                configuration["EntityExtraction:FivetoolsDataDirectory"] ?? "5etools"));
        services.AddSingleton<EntityNameMatcher>();
        services.AddSingleton<EntityCandidateBuilder>();
        // Scoped, not Singleton: EntityExtractionRunner now depends on GroundingCascade, which is
        // Scoped (it in turn depends on the Scoped ITier1Grounding/IGroundingJudge). A Singleton
        // cannot consume a Scoped service without becoming a captive dependency. Its only consumer,
        // IEntityExtractionOrchestrator, is already Scoped, so this is a safe lifetime change.
        services.AddScoped<EntityExtractionRunner>();
        services.AddSingleton<IReadOnlyDictionary<EntityType, EntityBackfillService>>(sp =>
        {
            var registry = sp.GetRequiredService<BookSourceRegistry>();
            var loader = sp.GetRequiredService<CanonicalJsonLoader>();
            var canonicalDir = sp.GetRequiredService<IOptions<EntityExtractionOptions>>().Value.CanonicalDirectory;
            var fivetoolsDir = configuration["EntityExtraction:FivetoolsDataDirectory"] ?? "5etools";
            IFivetoolsBackfillProvider[] providers =
                { new MonsterBackfillProvider(), new SpellBackfillProvider(), new MagicItemBackfillProvider(), new GodBackfillProvider() };
            return providers.ToDictionary(p => p.Type, p => new EntityBackfillService(p, registry, loader, canonicalDir, fivetoolsDir));
        });
        services.AddScoped<IEntityExtractionOrchestrator, EntityExtractionOrchestrator>();
        services.AddSingleton<DndMcpAICsharpFun.Features.Admin.CanonicalValidationService>();
        services.AddScoped<DndMcpAICsharpFun.Features.Admin.CanonicalTypeFixerService>();
        services.AddScoped<DndMcpAICsharpFun.Features.Admin.CanonicalNameNormalizerService>();
        services.AddScoped<DndMcpAICsharpFun.Features.Admin.NeedsReviewService>();
        services.AddScoped<DndMcpAICsharpFun.Features.Admin.BookRegistrationService>();
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
