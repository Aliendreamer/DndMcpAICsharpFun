using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.Authority;

/// <summary>
/// Refute-biased authority referee for sourceless (keyless-book) extraction candidates.
/// Answers the AUTHORITY question — "is this a real/known thing or the author's invention" — which
/// is orthogonal to the grounding cascade's field-truth question. It NEVER drops an entity: a
/// confirming authoritative web hit upgrades the label to <c>verified-thirdparty</c>; anything else
/// (a miss, a timeout, the toggle being off) leaves it <c>homebrew</c>.
/// </summary>
public interface IWebAuthorityReferee
{
    /// <summary>
    /// Returns true only on a strong authoritative-looking hit (→ <c>verified-thirdparty</c>);
    /// false on a miss/timeout/disabled (→ <c>homebrew</c>). Must not throw for a web failure.
    /// </summary>
    Task<bool> IsAuthoritativeAsync(string entityName, EntityType type, CancellationToken ct);
}
