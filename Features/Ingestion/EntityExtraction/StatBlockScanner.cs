using System.Collections.Frozen;
using System.Text.RegularExpressions;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;

namespace DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;

/// <summary>
/// Detects monster stat blocks directly from the structure stream by their signature
/// (a <c>&lt;size&gt; &lt;type&gt;, &lt;alignment&gt;</c> line immediately followed by an
/// "Armor Class … Hit Points …" line), independent of section-header detection. This recovers
/// stat blocks that MinerU failed to tag with a heading — or fragmented under mis-detected
/// "ACTIONS"/"REACTIONS" headers — which the header-based <see cref="EntityCandidateScanner"/>
/// drops or misnames (validated against the Monster Manual: Hill/Fire/Frost/Stone/Cloud Giant,
/// Gelatinous Cube, elementals). The monster name is taken from the nearest preceding
/// non-internal section header. Emitted candidates are deduped by the orchestrator's id-keyed
/// loop, so a header-clean stat block already captured by the section scanner is not duplicated.
/// </summary>
public sealed partial class StatBlockScanner
{
    [GeneratedRegex(
        @"^\s*(tiny|small|medium|large|huge|gargantuan)\s+[a-z]",
        RegexOptions.IgnoreCase)]
    private static partial Regex SizeTypeLine();

    // Stat-block-internal sub-headings MinerU often mis-detects as section headers; never a name.
    private static readonly FrozenSet<string> InternalHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "actions", "reactions", "legendary actions", "lair actions", "regional effects", "traits",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    // A monster name sits in a header close to its stat block. A far-back header is a different
    // monster (MinerU sometimes strips a stat block's name entirely, e.g. the MM "true giants"
    // stacked back-to-back) — naming from it would be wrong, so we skip rather than mislabel.
    private const int NameLookbackItems = 18;

    public IEnumerable<EntityCandidate> Scan(IReadOnlyList<PdfStructureItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        for (var i = 0; i < items.Count; i++)
        {
            if (!IsSizeTypeLine(items[i].Text)) continue;
            if (!HasArmorClassWithin(items, i, 2)) continue;

            var name = NearestName(items, i);
            if (name is null) continue;

            var end = StatBlockEnd(items, i);
            var text = string.Join("\n", Enumerable.Range(i, end - i + 1).Select(j => items[j].Text));

            yield return new EntityCandidate(
                Type: EntityType.Monster,
                DisplayName: name,
                Text: text,
                Page: items[i].PageNumber,
                // Offer Object alongside Monster: a stat block with AC/HP may be a non-creature
                // object (siege weapon, animated door). The model picks Object for those; the
                // keyword-derived primary Type stays Monster for stable identity/checkpointing.
                TypePrior: new[] { EntityType.Monster, EntityType.Object });

            i = end; // don't re-scan inside the stat block we just emitted
        }
    }

    private static bool IsSizeTypeLine(string text) =>
        !string.IsNullOrWhiteSpace(text) && SizeTypeLine().IsMatch(text);

    private static bool HasArmorClassWithin(IReadOnlyList<PdfStructureItem> items, int i, int window)
    {
        for (var j = i + 1; j <= i + window && j < items.Count; j++)
            if (ExtractionSignatures.HasArmorClass(items[j].Text))
                return true;
        return false;
    }

    private static string? NearestName(IReadOnlyList<PdfStructureItem> items, int i)
    {
        var lower = Math.Max(0, i - NameLookbackItems);
        for (var j = i - 1; j >= lower; j--)
        {
            if (!IsHeader(items[j].Type)) continue;
            var t = items[j].Text.Trim();
            if (t.Length == 0) continue;
            if (InternalHeaders.Contains(NormalizeHeader(t))) continue;
            if (!t.Any(char.IsLetter)) continue;
            // A name is short; a sentence-like header is not a monster name.
            if (t.Length > 48 || t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 6) continue;
            return t;
        }
        return null;
    }

    private static int StatBlockEnd(IReadOnlyList<PdfStructureItem> items, int start)
    {
        for (var j = start + 1; j < items.Count; j++)
        {
            // next stat block (next monster) ends this one
            if (IsSizeTypeLine(items[j].Text) && HasArmorClassWithin(items, j, 2))
                return j - 1;
            // a non-internal section header ends this one
            if (IsHeader(items[j].Type) && !InternalHeaders.Contains(NormalizeHeader(items[j].Text)))
                return j - 1;
        }
        return items.Count - 1;
    }

    private static bool IsHeader(string? type) =>
        type is not null &&
        (type.Contains("header", StringComparison.OrdinalIgnoreCase) ||
         type.Contains("section", StringComparison.OrdinalIgnoreCase) ||
         type.Equals("title", StringComparison.OrdinalIgnoreCase));

    // Collapse OCR spacing/case so "ACTIONS"/"AcTIONS"/"Legendary  Actions" match the internal set.
    private static string NormalizeHeader(string text) =>
        Regex.Replace(text.Trim().ToLowerInvariant(), @"\s+", " ");
}
