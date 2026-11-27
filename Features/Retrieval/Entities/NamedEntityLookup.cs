using System.Text.RegularExpressions;

using DndMcpAICsharpFun.Features.VectorStore.Entities;

namespace DndMcpAICsharpFun.Features.Retrieval.Entities;

/// <summary>
/// Finds the entities a question names: by the entity's own name ("Gloom Stalker") or by a feature inside its text
/// ("Flurry of Blows" in the monk). Vector search misses these when the rest of the question reads like something
/// else; the found entities join the search's candidates. The books decide what is a name (a name, or a phrase
/// printed capitalised); the question and the names compare in one normal form (lower case, single spaces), so
/// "flurry of blows" and "Flurry of Blows" find the same. Built from the store on first use, rebuilt when stale.
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
    private const int MaxPhraseWords = 5;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Snapshot? _snapshot;

    private sealed record Entry(string Id, string Raw, string SourceBook);
    private sealed record Snapshot(
        ILookup<string, Entry> ByName, IReadOnlyDictionary<string, List<Entry>> ByPhrase, DateTimeOffset BuiltAt);

    /// <summary>The ids the query names, with the length of the naming phrase; a whole-name match first.</summary>
    public async Task<IReadOnlyList<(string Id, int NameLength)>> FindAsync(
        string query, string? sourceBook, CancellationToken ct)
    {
        var phrases = Phrases(query);
        if (phrases.Count == 0) return [];
        var snap = await GetAsync(ct);
        bool InBook(Entry e) => sourceBook is null || string.Equals(e.SourceBook, sourceBook, StringComparison.OrdinalIgnoreCase);

        var found = new Dictionary<string, int>(StringComparer.Ordinal);
        void Keep(string id, int rank) => found[id] = Math.Max(found.GetValueOrDefault(id), rank);
        foreach (var phrase in phrases)
        {
            foreach (var e in snap.ByName[phrase].Where(InBook)) Keep(e.Id, 1000 + phrase.Length);
            if (!snap.ByPhrase.TryGetValue(phrase, out var holders) || holders.Count > MaxTextMatches) continue;
            // The entry that heads the feature outranks those that only mention it ("your Flurry of Blows").
            var heading = Heading(phrase);
            foreach (var e in holders.Where(InBook)) Keep(e.Id, phrase.Length + (heading.IsMatch(e.Raw) ? 1 : 0));
        }
        return [.. found.OrderByDescending(kv => kv.Value).Select(kv => (kv.Key, kv.Value))];
    }

    // The phrase as a heading: opening a line before "." or ":", or run into the next sentence's capital
    // ("…modifier Flurry of Blows Immediately after…", where the line break was lost).
    private static Regex Heading(string phrase)
    {
        var words = string.Join(@"\W+", phrase.Split(' ').Select(Regex.Escape));
        return new Regex($@"(?im:^{words}[.:]\s)|(?i:\b{words})(?-i: [A-Z][a-z])", RegexOptions.None, TimeSpan.FromSeconds(1));
    }

    /// <summary>Every run of two to five words of the question, in normal form, longest first.</summary>
    internal static IReadOnlyList<string> Phrases(string query)
    {
        var words = Normal(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var phrases = new List<string>();
        for (var n = Math.Min(MaxPhraseWords, words.Length); n >= 2; n--)
            for (var i = 0; i + n <= words.Length; i++)
                phrases.Add(string.Join(' ', words, i, n));
        return [.. phrases.Distinct(StringComparer.Ordinal)];
    }

    /// <summary>The names a text prints: runs of capitalised words (with of/the/and between them), and every
    /// part of a run that starts and ends on a capitalised word — "Flurry of Blows Immediately" holds "Flurry of Blows".</summary>
    internal static IEnumerable<string> PrintedNames(string text)
    {
        foreach (Match run in CapitalisedRun().Matches(text))
        {
            var words = run.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < words.Length; i++)
            {
                if (!char.IsUpper(words[i][0])) continue;
                for (var j = i + 1; j < Math.Min(words.Length, i + MaxPhraseWords); j++)
                    if (char.IsUpper(words[j][0]))
                        yield return Normal(string.Join(' ', words[i..(j + 1)]));
            }
        }
    }

    [GeneratedRegex(@"\b[A-Z][\p{L}'’-]*(?:[ \n]+(?:of|the|and|[A-Z][\p{L}'’-]*))+")]
    private static partial Regex CapitalisedRun();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NonWord();

    // Lower case, every run of punctuation and space one space: "Ki-Empowered Strikes" and OCR line breaks compare.
    internal static string Normal(string s) => NonWord().Replace(s.ToLowerInvariant(), " ").Trim();

    private async Task<Snapshot> GetAsync(CancellationToken ct)
    {
        var snap = _snapshot;
        if (snap is not null && DateTimeOffset.UtcNow - snap.BuiltAt < MaxAge) return snap;
        await _gate.WaitAsync(ct);
        try
        {
            if (_snapshot is { } s && DateTimeOffset.UtcNow - s.BuiltAt < MaxAge) return s;
            var all = await _scrollAll(ct);
            var byName = new List<(string Name, Entry Entry)>();
            var byPhrase = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
            foreach (var h in all)
            {
                var e = new Entry(h.Envelope.Id, h.Envelope.CanonicalText, h.Envelope.SourceBook);
                var name = Normal(h.Envelope.Name);
                if (name.Contains(' ')) byName.Add((name, e));   // one word names too much ("fire", "level")
                foreach (var phrase in PrintedNames(h.Envelope.CanonicalText).Distinct(StringComparer.Ordinal))
                {
                    if (!byPhrase.TryGetValue(phrase, out var list)) byPhrase[phrase] = list = [];
                    if (list.Count <= MaxTextMatches) list.Add(e);   // past the cap the phrase is skipped anyway
                }
            }
            _snapshot = new Snapshot(byName.ToLookup(x => x.Name, x => x.Entry, StringComparer.Ordinal), byPhrase,
                DateTimeOffset.UtcNow);
            _logger.LogInformation("Named-entity lookup built: {Count} entities, {Phrases} printed names",
                all.Count, byPhrase.Count);
            return _snapshot;
        }
        finally { _gate.Release(); }
    }
}