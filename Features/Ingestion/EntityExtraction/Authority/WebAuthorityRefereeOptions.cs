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
    /// "5e.tools" also matches "www.5e.tools".</summary>
    public string[] AuthoritativeDomains { get; init; } =
    [
        "5e.tools",
        "dnd5e.wikidot.com",
        "roll20.net",
        "dndbeyond.com",
    ];

    /// <summary>Refute threshold: the minimum number of authoritative, name-matching hits required
    /// to confirm an entity as <c>verified-thirdparty</c>. Higher = stricter (more refute-biased).</summary>
    public int MinAuthoritativeHits { get; init; } = 1;
}
