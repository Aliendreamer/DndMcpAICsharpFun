namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.Authority;

/// <summary>
/// Configuration for the Tier-3 web authority referee (extraction-authority-ladder).
/// Bound from the "WebAuthorityReferee" config section. The referee is OFF by default: it is a
/// bounded, opt-in cross-check that only labels sourceless candidates and never drops an entity.
/// Enable live (per design.md, task 4.3) via config or the <c>WebAuthorityReferee__Enabled=true</c>
/// environment override.
/// </summary>
public sealed class WebAuthorityRefereeOptions
{
    /// <summary>Master toggle. When false the referee makes NO web calls and every keyless
    /// candidate stays <c>homebrew</c>.</summary>
    public bool Enabled { get; init; }

    /// <summary>Per-adjudication wall-clock budget. A timeout is treated as a miss (→ homebrew),
    /// never a drop.</summary>
    public int TimeoutSeconds { get; init; } = 5;

    /// <summary>The authoritative domains a confirming hit must come from. A web result on any
    /// other domain is ignored by the refute-biased confirm test. Matched on host suffix, so
    /// "5esrd.com" also matches "www.5esrd.com".
    /// Tuned from a live SearXNG probe (T3 task 4.3): the shared engine (DuckDuckGo-backed) actually
    /// surfaces 5e.tools, 5esrd.com, and the D&D Fandom/wikidot wikis for real entities, while
    /// dndbeyond.com / roll20.net rarely appear (they block scrapers) — so those are kept only as
    /// harmless extras. Override the whole list per-deployment via config.</summary>
    public string[] AuthoritativeDomains { get; init; } =
    [
        "5e.tools",
        "5esrd.com",
        "dnd5e.wikidot.com",
        "forgottenrealms.fandom.com",
        "dnd-5e.fandom.com",
        "roll20.net",
        "dndbeyond.com",
    ];

    /// <summary>Refute threshold: the minimum number of authoritative, name-matching hits required
    /// to confirm an entity as <c>verified-thirdparty</c>. Higher = stricter (more refute-biased).</summary>
    public int MinAuthoritativeHits { get; init; } = 1;

    /// <summary>SearXNG engines the referee rotates over, ONE per query in round-robin order, so no
    /// single upstream engine is bursted into rate-limiting (the `403 suspended` noise seen in T3
    /// live validation). A live probe found only these two return useful D&D results — google /
    /// startpage / brave / bing block SearXNG's datacenter-IP scraping and return empty or junk.
    /// Empty = don't pin (query SearXNG's full default engine set, the pre-tuning behavior).</summary>
    public string[] Engines { get; init; } = ["duckduckgo", "presearch"];
}
