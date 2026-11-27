using System.Text.RegularExpressions;

using DndMcpAICsharpFun.Features.VectorStore.Entities;

namespace DndMcpAICsharpFun.Features.Retrieval.Entities;

/// <summary>
/// Finds the entities a question names: by the entity's own name ("Gloom Stalker") or by a feature inside its text
/// ("Flurry of Blows" in the monk). Vector search misses these when the rest of the question reads like something
/// else; the found entities join the search's candidates. Built from the store on first use, rebuilt when stale.
/// </summary>
public sealed partial class NamedEntityLookup
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<EntitySearchHit>>> _scrollAll;
    private readonly ILogger<NamedEntityLookup> _logger;

    // The store is scoped; this cache lives for the app, so each rebuild reads it from a scope of its own.
    public NamedEntityLookup(IServiceScopeFactory scopes, ILogger<NamedEntityLookup> logger)
        : this(async ct =>
        {
            using var scope = scopes.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IEntityVectorStore>().ScrollAllAsync(ct);
        }, logger)
    { }

    internal NamedEntityLookup(IEntityVectorStore store, ILogger<NamedEntityLookup> logger)
        : this(store.ScrollAllAsync, logger) { }

    private NamedEntityLookup(Func<CancellationToken, Task<IReadOnlyList<EntitySearchHit>>> scrollAll, ILogger<NamedEntityLookup> logger)
    {
        _scrollAll = scrollAll;
        _logger = logger;
    }

    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);
    // A phrase in more entities than this names nothing in particular.
    private const int MaxTextMatches = 6;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Snapshot? _snapshot;

    private sealed record Entry(string Id, string Name, string Text, string Raw, string SourceBook);
    private sealed record Snapshot(IReadOnlyList<Entry> Entries, ILookup<string, Entry> ByName, DateTimeOffset BuiltAt);

    /// <summary>The ids the query names, with the length of the naming phrase; a whole-name match first.</summary>
    public async Task<IReadOnlyList<(string Id, int NameLength)>> FindAsync(
        string query, string? sourceBook, CancellationToken ct)
    {
        var phrases = Phrases(query);
        if (phrases.Count == 0) return [];
        var snap = await GetAsync(ct);
        bool InBook(Entry e) => sourceBook is null || string.Equals(e.SourceBook, sourceBook, StringComparison.OrdinalIgnoreCase);

        var found = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var phrase in phrases.OrderByDescending(p => p.Length))
        {
            var byName = snap.ByName[Normal(phrase)].Where(InBook).ToList();
            foreach (var e in byName) Keep(e.Id, 1000 + phrase.Length);
            if (byName.Count > 0) continue;
            var needle = " " + Normal(phrase) + " ";
            var inText = snap.Entries.Where(e => InBook(e) && e.Text.Contains(needle, StringComparison.Ordinal))
                .Take(MaxTextMatches + 1).ToList();
            if (inText.Count > MaxTextMatches) continue;
            // The entry that heads the feature outranks those that only mention it ("your Flurry of Blows").
            var heading = Heading(phrase);
            foreach (var e in inText) Keep(e.Id, phrase.Length + (heading.IsMatch(e.Raw) ? 1 : 0));
        }
        void Keep(string id, int rank) => found[id] = Math.Max(found.GetValueOrDefault(id), rank);
        return [.. found.OrderByDescending(kv => kv.Value).Select(kv => (kv.Key, kv.Value))];
    }

    // The phrase as a heading: opening a line before "." or ":", or run into the next sentence's capital
    // ("…modifier Flurry of Blows Immediately after…", where the line break was lost).
    private static Regex Heading(string phrase)
    {
        var words = string.Join(@"\s+", phrase.Split(' ').Select(Regex.Escape));
        return new Regex($@"(?im:^{words}[.:]\s)|(?i:\b{words})(?-i: [A-Z][a-z])", RegexOptions.None, TimeSpan.FromSeconds(1));
    }

    /// <summary>Capitalised runs of two to five words ("Flurry of Blows", "Gloom Stalker's" → "Gloom Stalker").</summary>
    internal static IReadOnlyList<string> Phrases(string query) =>
        [.. CapitalisedRun().Matches(query)
            .Select(m => TrailingSmallWords().Replace(m.Value.Replace("'s", "").Replace("’s", ""), ""))
            .Where(p => p.Contains(' '))
            .Distinct(StringComparer.OrdinalIgnoreCase)];

    [GeneratedRegex(@"\b[A-Z][\p{L}'’-]*(?: (?:of|the|and|[A-Z][\p{L}'’-]*)){1,4}")]
    private static partial Regex CapitalisedRun();

    [GeneratedRegex(@"(?: (?:of|the|and))+$")]
    private static partial Regex TrailingSmallWords();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWord();

    // Lower case, every run of punctuation and space one space: "Ki-Empowered Strikes" and OCR line breaks compare.
    private static string Normal(string s) => NonWord().Replace(s.ToLowerInvariant(), " ").Trim();

    private async Task<Snapshot> GetAsync(CancellationToken ct)
    {
        var snap = _snapshot;
        if (snap is not null && DateTimeOffset.UtcNow - snap.BuiltAt < MaxAge) return snap;
        await _gate.WaitAsync(ct);
        try
        {
            if (_snapshot is { } s && DateTimeOffset.UtcNow - s.BuiltAt < MaxAge) return s;
            var all = await _scrollAll(ct);
            var entries = all.Select(h => new Entry(
                h.Envelope.Id, Normal(h.Envelope.Name), " " + Normal(h.Envelope.CanonicalText) + " ",
                h.Envelope.CanonicalText, h.Envelope.SourceBook))
                .ToList();
            _snapshot = new Snapshot(entries, entries.ToLookup(e => e.Name, StringComparer.Ordinal), DateTimeOffset.UtcNow);
            _logger.LogInformation("Named-entity lookup built: {Count} entities", entries.Count);
            return _snapshot;
        }
        finally { _gate.Release(); }
    }
}