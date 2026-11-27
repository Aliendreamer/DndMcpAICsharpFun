using System.Text.RegularExpressions;

using DndMcpAICsharpFun.Domain.Entities;
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
        ILookup<string, Entry> ByName, ILookup<string, Entry> ByWordName,
        IReadOnlyDictionary<string, List<Entry>> ByPhrase, IReadOnlyDictionary<string, List<Entry>> ByHeadingWord,
        DateTimeOffset BuiltAt);

    /// <summary>The ids the query names, with the length of the naming phrase; a whole-name match first.</summary>
    public async Task<IReadOnlyList<(string Id, int NameLength)>> FindAsync(
        string query, string? sourceBook, CancellationToken ct)
    {
        var phrases = Phrases(query);
        var words = CapitalisedWords(query);
        if (phrases.Count == 0 && words.Count == 0) return [];
        var snap = await GetAsync(ct);
        bool InBook(Entry e) => sourceBook is null || string.Equals(e.SourceBook, sourceBook, StringComparison.OrdinalIgnoreCase);
        // The book is the model's guess: a name it does not hold ("Samurai" with PHB) is found in the book that does.
        IEnumerable<Entry> Named(IEnumerable<Entry> all) { var inBook = all.Where(InBook).ToList(); return inBook.Count > 0 ? inBook : all; }

        var found = new Dictionary<string, int>(StringComparer.Ordinal);
        void Keep(string id, int rank) => found[id] = Math.Max(found.GetValueOrDefault(id), rank);
        foreach (var phrase in phrases)
        {
            foreach (var e in Named(snap.ByName[phrase])) Keep(e.Id, 1000 + phrase.Length);
            if (!snap.ByPhrase.TryGetValue(phrase, out var holders) || holders.Count > MaxTextMatches) continue;
            // The entry that heads the feature outranks those that only mention it ("your Flurry of Blows").
            var heading = Heading(phrase);
            foreach (var e in holders.Where(InBook)) Keep(e.Id, phrase.Length + (heading.IsMatch(e.Raw) ? 1 : 0));
        }
        // One word names too much in lower case ("fire", "level"); written capitalised it names a thing:
        // "Samurai" the subclass, "Indomitable" the feature that heads a paragraph of the fighter.
        foreach (var word in words)
        {
            var byName = snap.ByWordName[word].ToList();
            if (byName.Count <= MaxTextMatches) foreach (var e in Named(byName)) Keep(e.Id, 1000 + word.Length);
            if (snap.ByHeadingWord.TryGetValue(word, out var heads) && heads.Count <= MaxTextMatches)
                foreach (var e in heads.Where(InBook)) Keep(e.Id, word.Length + 1);
        }
        return [.. found.OrderByDescending(kv => kv.Value).Select(kv => (kv.Key, kv.Value))];
    }

    /// <summary>The query's words written with a capital, of four letters or more, in normal form.</summary>
    internal static IReadOnlyList<string> CapitalisedWords(string query) =>
        [.. CapitalisedWord().Matches(query).Select(m => Normal(m.Value)).Distinct(StringComparer.Ordinal)];

    [GeneratedRegex(@"(?<![\p{L}'’-])\p{Lu}[\p{L}'’-]{3,}")]
    private static partial Regex CapitalisedWord();

    /// <summary>The one-word headings a text prints: a paragraph opened by a single word and a full stop
    /// ("Indomitable. Beginning at 9th level…"), at a line start or after a sentence (lost line breaks).</summary>
    internal static IEnumerable<string> HeadingWords(string text) =>
        HeadingWord().Matches(text).Select(m => Normal(m.Groups[1].Value)).Distinct(StringComparer.Ordinal);

    [GeneratedRegex(@"(?:^|[.!?]\s+)(\p{Lu}[\p{L}'’-]{3,})(?: \([^)\n]*\))?\.\s+\p{Lu}", RegexOptions.Multiline)]
    private static partial Regex HeadingWord();

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
            var byWordName = new List<(string Name, Entry Entry)>();
            var byPhrase = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
            var byHeadingWord = new Dictionary<string, List<Entry>>(StringComparer.Ordinal);
            static void Add(Dictionary<string, List<Entry>> index, string key, Entry e)
            {
                if (!index.TryGetValue(key, out var list)) index[key] = list = [];
                if (list.Count <= MaxTextMatches) list.Add(e);   // past the cap the key is skipped anyway
            }
            foreach (var h in all)
            {
                var e = new Entry(h.Envelope.Id, h.Envelope.CanonicalText, h.Envelope.SourceBook);
                var name = Normal(h.Envelope.Name);
                if (name.Contains(' ')) byName.Add((name, e));
                // A one-word Rule or Lore title is a topic ("Fire", "Cover"), not a thing the question names.
                else if (h.Envelope.Type is not (EntityType.Rule or EntityType.Lore)) byWordName.Add((name, e));
                foreach (var phrase in PrintedNames(h.Envelope.CanonicalText).Distinct(StringComparer.Ordinal))
                    Add(byPhrase, phrase, e);
                if (h.Envelope.Type is not (EntityType.Rule or EntityType.Lore))
                    foreach (var word in HeadingWords(h.Envelope.CanonicalText)) Add(byHeadingWord, word, e);
            }
            _snapshot = new Snapshot(byName.ToLookup(x => x.Name, x => x.Entry, StringComparer.Ordinal),
                byWordName.ToLookup(x => x.Name, x => x.Entry, StringComparer.Ordinal), byPhrase, byHeadingWord,
                DateTimeOffset.UtcNow);
            _logger.LogInformation("Named-entity lookup built: {Count} entities, {Phrases} printed names",
                all.Count, byPhrase.Count);
            return _snapshot;
        }
        finally { _gate.Release(); }
    }
}