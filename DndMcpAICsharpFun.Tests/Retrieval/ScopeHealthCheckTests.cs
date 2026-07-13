using DndMcpAICsharpFun.Features.Downtime;
using DndMcpAICsharpFun.Features.Lore;
using DndMcpAICsharpFun.Features.Retrieval;
using DndMcpAICsharpFun.Features.Rules;
using DndMcpAICsharpFun.Features.VectorStore;
using DndMcpAICsharpFun.Tests.TestDoubles;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NSubstitute;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Retrieval;

/// <summary>
/// Startup guard (Task 6): warns, non-fatally, when a scoped retrieval key (RuleSources,
/// DowntimeSources, SettingCatalog) has zero blocks in dnd_blocks — the check that would have
/// caught the DMG-never-ingested gap in the live corpus.
/// </summary>
public sealed class ScopeHealthCheckTests
{
    [Fact]
    public void WarnOnZeroCounts_logs_a_warning_naming_the_zero_count_key()
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["PHB"] = 100,
            ["DMG"] = 0,
            ["MM"] = 50,
            ["XGE"] = 20,
            ["ERLW"] = 30,
        };
        var scopeKeys = new[] { "PHB", "DMG", "MM", "XGE", "ERLW" };
        var capturingLogger = new CapturingLogger<ScopeHealthCheck>();

        var act = () => ScopeHealthCheck.WarnOnZeroCounts(scopeKeys, counts, capturingLogger);

        act.Should().NotThrow();
        capturingLogger.Logs.Should().ContainSingle(
            l => l.Level == LogLevel.Warning && l.Message.Contains("DMG"),
            "a warning must be logged naming the zero-count scope key");
    }

    [Fact]
    public void WarnOnZeroCounts_logs_a_warning_for_a_missing_key_too()
    {
        // A key absent from the counts dictionary entirely (not just zero) must also warn.
        var counts = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["PHB"] = 100,
        };
        var scopeKeys = new[] { "PHB", "DMG" };
        var capturingLogger = new CapturingLogger<ScopeHealthCheck>();

        ScopeHealthCheck.WarnOnZeroCounts(scopeKeys, counts, capturingLogger);

        capturingLogger.Logs.Should().ContainSingle(
            l => l.Level == LogLevel.Warning && l.Message.Contains("DMG"));
    }

    [Fact]
    public void WarnOnZeroCounts_does_not_warn_when_all_scope_keys_have_blocks()
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["PHB"] = 100,
            ["DMG"] = 5,
        };
        var scopeKeys = new[] { "PHB", "DMG" };
        var capturingLogger = new CapturingLogger<ScopeHealthCheck>();

        ScopeHealthCheck.WarnOnZeroCounts(scopeKeys, counts, capturingLogger);

        capturingLogger.Logs.Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_does_not_throw_when_the_vector_store_call_fails()
    {
        var vectorStore = Substitute.For<IVectorStoreService>();
        vectorStore.GetSourceKeyCountsAsync(Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyDictionary<string, long>>>(_ => throw new InvalidOperationException("Qdrant unreachable"));

        var services = new ServiceCollection();
        services.AddSingleton(vectorStore);
        await using var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var capturingLogger = new CapturingLogger<ScopeHealthCheck>();
        var check = new ScopeHealthCheck(scopeFactory, capturingLogger);

        await FluentActions.Awaiting(() => check.StartAsync(CancellationToken.None))
            .Should().NotThrowAsync();
        capturingLogger.Logs.Should().Contain(l => l.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task StartAsync_warns_for_a_zero_count_scope_key_end_to_end()
    {
        var counts = BookCatalog.Keys.ToDictionary(k => k, k => k == "DMG" ? 0L : 10L, StringComparer.Ordinal);
        var vectorStore = Substitute.For<IVectorStoreService>();
        vectorStore.GetSourceKeyCountsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<string, long>>(counts));

        var services = new ServiceCollection();
        services.AddSingleton(vectorStore);
        await using var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var capturingLogger = new CapturingLogger<ScopeHealthCheck>();
        var check = new ScopeHealthCheck(scopeFactory, capturingLogger);

        await check.StartAsync(CancellationToken.None);

        capturingLogger.Logs.Should().ContainSingle(
            l => l.Level == LogLevel.Warning && l.Message.Contains("DMG"));
    }

    [Fact]
    public async Task StopAsync_completes_immediately()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IVectorStoreService>());
        await using var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
        var check = new ScopeHealthCheck(scopeFactory, new CapturingLogger<ScopeHealthCheck>());

        var task = check.StopAsync(CancellationToken.None);

        task.IsCompletedSuccessfully.Should().BeTrue();
    }

    // ── Consistency guard ────────────────────────────────────────────────────

    [Fact]
    public void Every_scope_key_across_RuleSources_DowntimeSources_and_SettingCatalog_is_a_BookCatalog_key()
    {
        var allScopeKeys = RuleSources.Keys
            .Concat(DowntimeSources.Keys)
            .Concat(SettingCatalog.AllScopeKeys)
            .ToHashSet(StringComparer.Ordinal);

        allScopeKeys.Should().NotBeEmpty();
        allScopeKeys.Should().BeSubsetOf(BookCatalog.Keys,
            "every scope key must resolve to a real book in the catalog, or the guard/scoping logic is silently wrong");
    }
}
