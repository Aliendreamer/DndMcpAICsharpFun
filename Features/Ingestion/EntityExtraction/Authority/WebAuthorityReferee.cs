using System.Collections.Concurrent;
using System.Text;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Search;

using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.Authority;

/// <summary>
/// Default <see cref="IWebAuthorityReferee"/> over the existing <see cref="SearXNGClient"/>.
/// Refute-biased: it confirms an entity only when at least <c>MinAuthoritativeHits</c> results come
/// from a configured authoritative domain AND actually name the entity; every other outcome (no
/// results, non-authoritative results, a per-call timeout, a transport error, or the toggle being
/// off) is a miss (→ <c>homebrew</c>). It NEVER drops an entity. Verdicts are cached by normalized
/// name so a re-extract of the same corpus makes no duplicate web calls.
/// </summary>
public sealed class WebAuthorityReferee(
    SearXNGClient client,
    IOptions<WebAuthorityRefereeOptions> options,
    ILogger<WebAuthorityReferee> logger) : IWebAuthorityReferee
{
    private readonly WebAuthorityRefereeOptions _opts = options.Value;
    private readonly ConcurrentDictionary<string, bool> _cache = new(StringComparer.Ordinal);
    private int _engineCursor = -1;

    public async Task<bool> IsAuthoritativeAsync(string entityName, EntityType type, CancellationToken ct)
    {
        // Toggle off: make NO web call and leave the entity homebrew.
        if (!_opts.Enabled || string.IsNullOrWhiteSpace(entityName)) return false;

        var key = NormalizeAlnum(entityName);
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var verdict = await AdjudicateUncachedAsync(entityName, type, ct);
        _cache[key] = verdict;
        return verdict;
    }

    private async Task<bool> AdjudicateUncachedAsync(string entityName, EntityType type, CancellationToken ct)
    {
        // Per-call timeout, treated as a miss (not a drop). A caller-driven cancellation still
        // propagates as cancellation; only OUR timeout is swallowed into a homebrew verdict.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _opts.TimeoutSeconds)));

        var query = $"\"{entityName}\" D&D 5e {type}";
        try
        {
            var results = await client.SearchAsync(query, timeoutCts.Token, NextEngine());
            var wanted = NormalizeAlnum(entityName);
            var authoritativeHits = results.Count(r =>
                IsAuthoritativeDomain(r.Url) &&
                NormalizeAlnum(r.Title + " " + r.Snippet).Contains(wanted, StringComparison.Ordinal));

            return authoritativeHits >= Math.Max(1, _opts.MinAuthoritativeHits);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // caller cancelled — respect it
        }
        catch (OperationCanceledException)
        {
            logger.LogDebug("Web authority referee timed out for '{Entity}'; treating as homebrew.", entityName);
            return false; // our per-call timeout — a miss, never a drop
        }
    }

    private bool IsAuthoritativeDomain(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        return _opts.AuthoritativeDomains.Any(d =>
            uri.Host.Equals(d, StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase));
    }

    // Round-robin ONE engine per query over the configured pool so no single upstream engine is
    // bursted into rate-limiting. Returns null (query all default engines) when the pool is empty.
    private string? NextEngine()
    {
        if (_opts.Engines.Length == 0) return null;
        var i = Interlocked.Increment(ref _engineCursor) & int.MaxValue;
        return _opts.Engines[i % _opts.Engines.Length];
    }

    // Reduce to lowercase alphanumerics so "Path of the Battlerager" matches "path-of-the-battlerager"
    // etc. across punctuation/spacing differences between the candidate name and page text.
    private static string NormalizeAlnum(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }
}
