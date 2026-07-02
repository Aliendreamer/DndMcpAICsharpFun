using DndMcpAICsharpFun.Extensions;
using DndMcpAICsharpFun.Features.Retrieval;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DndMcpAICsharpFun.Tests.Di;

/// <summary>
/// F5 — Full-container DI scope/build validation.
///
/// Builds the application's service collection via the real Add* extension methods,
/// then calls BuildServiceProvider with ValidateScopes = true and ValidateOnBuild = true.
/// Any scoped-from-singleton lifetime mistake (like the FusedRetrievalService regression)
/// will throw here rather than reaching runtime.
///
/// External clients that would attempt real network connections (QdrantClient,
/// OllamaApiClient, AppDbContext, MinerU HttpClient) are never instantiated because
/// ValidateOnBuild only validates the descriptor graph — it does not invoke factory
/// lambdas for singletons.  Options [Required] validators fire on IHost.StartAsync,
/// not on BuildServiceProvider, so they do not affect this test.
/// </summary>
public sealed class FullContainerScopeValidationTests
{
    /// <summary>
    /// Builds the full internal service graph (all Add* extension groups) and asserts
    /// that BuildServiceProvider does not throw a DI scope/build validation error.
    /// </summary>
    [Fact]
    public void BuildServiceProvider_WithAllExtensionGroups_DoesNotThrow()
    {
        // Arrange — minimal in-memory config so extension methods that read config
        // values directly (e.g. AddDndChat, AddDatabase) receive non-null strings.
        var config = BuildMinimalConfig();
        var services = BuildServiceCollection(config);

        // Act — ValidateOnBuild validates the graph (missing deps + lifetime rules).
        // ValidateScopes catches scoped-from-singleton at resolve time.
        var act = () => services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true,
            });

        // Assert
        act.Should().NotThrow(
            "the composed service collection must have no missing-dependency " +
            "or scoped-from-singleton violations");
    }

    /// <summary>
    /// Regression guard: confirms that the ValidateOnBuild mechanism itself is effective
    /// by verifying that a deliberately broken registration (scoped consumed by singleton)
    /// causes BuildServiceProvider to throw.  If this test fails, the guard is broken.
    /// </summary>
    [Fact]
    public void BuildServiceProvider_WithScopedConsumedBySingleton_Throws()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<_ScopedDep>();
        services.AddSingleton<_SingletonConsumingScoped>();

        var act = () => services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true,
            });

        act.Should().Throw<AggregateException>(
            "a scoped dependency consumed by a singleton must be detected at build time");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IConfiguration BuildMinimalConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // QdrantOptions — Host is [Required]
                ["Qdrant:Host"] = "localhost",
                // OllamaOptions — three [Required] fields
                ["Ollama:BaseUrl"] = "http://localhost:11434",
                ["Ollama:EmbeddingModel"] = "test-embed",
                ["Ollama:ChatModel"] = "test-chat",
                // EntityExtractionOptions — two [Required] fields
                ["EntityExtraction:CanonicalDirectory"] = "books/canonical",
                ["EntityExtraction:SchemasDirectory"] = "Schemas/canonical",
                // SearXNG — Url is [Required]
                ["SearXNG:Url"] = "http://localhost:8080",
                // AddDatabase / AddDndChat read these with GetValue, need a value.
                ["RateLimit:MessagesPerMinute"] = "10",
                ["RateLimit:RequestsPerMinute"] = "60",
            })
            .Build();

    private static ServiceCollection BuildServiceCollection(IConfiguration config)
    {
        var services = new ServiceCollection();

        // Logging — ILogger<T> is injected by many services.
        services.AddLogging();

        // IConfiguration — extension methods (AddDndChat, AddDatabase, …) accept it.
        services.AddSingleton<IConfiguration>(config);

        // Options registrations — mirroring Program.cs order, without ValidateOnStart
        // (that fires on IHostedService start, not on BuildServiceProvider).
        services.AddOptions<DndMcpAICsharpFun.Infrastructure.Qdrant.QdrantOptions>()
            .BindConfiguration("Qdrant")
            .ValidateDataAnnotations();
        services.AddOptions<DndMcpAICsharpFun.Infrastructure.Ollama.OllamaOptions>()
            .BindConfiguration("Ollama")
            .ValidateDataAnnotations();
        services.AddOptions<DndMcpAICsharpFun.Infrastructure.Ingestion.IngestionOptions>()
            .BindConfiguration("Ingestion")
            .ValidateDataAnnotations();
        services.AddOptions<DndMcpAICsharpFun.Features.Admin.AdminOptions>()
            .BindConfiguration("Admin")
            .ValidateDataAnnotations();
        services.AddOptions<RetrievalOptions>()
            .BindConfiguration("Retrieval")
            .ValidateDataAnnotations();
        services.AddOptions<DndMcpAICsharpFun.Features.Ingestion.Entities.EntityIngestionOptions>()
            .BindConfiguration("EntityIngestion")
            .ValidateDataAnnotations();
        services.AddOptions<DndMcpAICsharpFun.Features.Mcp.McpOptions>()
            .BindConfiguration("Mcp")
            .ValidateDataAnnotations();
        services.AddOptions<RerankerOptions>()
            .BindConfiguration("Reranker")
            .ValidateDataAnnotations();
        services.AddOptions<DndMcpAICsharpFun.Features.Chat.McpClientOptions>()
            .BindConfiguration("McpClient");
        services.AddOptions<DndMcpAICsharpFun.Infrastructure.Postgres.PostgresOptions>()
            .BindConfiguration("Postgres");


        // All internal Add* extension groups — same order as Program.cs.
        services.AddInfrastructureClients(config);
        services.AddIngestionPipeline();
        services.AddRetrieval();
        services.AddWebSearch(config);
        services.AddEntityExtraction(config);
        services.AddObservability(config);

        // Companion-layer service registrations.
        // AddDndBlazor (Razor components) requires ASP.NET Core server infrastructure
        // not available in a plain ServiceCollection — omitted intentionally.
        // AddMcpServer likewise requires the full host — omitted intentionally.

        // AddAuthorization (called by AddDndAuthentication) registers AuthorizationPolicyCache
        // which depends on EndpointDataSource — a routing infrastructure type.
        // AddRouting() provides it, matching what the full WebApplication host does.
        services.AddRouting();

        services.AddDatabase(config);
        services.AddDndChat(config);
        services.AddDndAuthentication();
        services.AddDndRateLimiting(config);

        return services;
    }

    // Helpers for the negative test only.
    private sealed class _ScopedDep;
#pragma warning disable CA1823 // CS9113 — parameter intentionally unused; it's the injection point under test
    private sealed class _SingletonConsumingScoped(_ScopedDep dep)
    {
        // dep is injected by DI; its value is not used — the point is that the
        // scoped type appears as a constructor parameter of a singleton.
        private readonly _ScopedDep _dep = dep;
    }
#pragma warning restore CA1823
}
