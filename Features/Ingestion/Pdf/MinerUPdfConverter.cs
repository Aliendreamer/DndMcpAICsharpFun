using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Ingestion.Pdf;

/// <summary>
/// Production MinerU-based <see cref="IPdfStructureConverter"/>. POSTs the PDF to the live
/// MinerU service (<c>POST {ServiceUrl}/file_parse</c>) and maps the returned
/// <c>content_list</c> typed blocks onto <see cref="PdfStructureDocument"/>:
/// <list type="bullet">
///   <item>a block carrying a <c>text_level</c> becomes a <c>section_header</c> item (heading candidate);</item>
///   <item>a plain <c>text</c> block becomes a <c>text</c> item;</item>
///   <item>headers / footers / page numbers / images / tables / equations are dropped.</item>
/// </list>
/// MinerU page indices are 0-based; they are shifted to 1-based to align with the bookmark TOC.
/// </summary>
public sealed class MinerUPdfConverter(
    IHttpClientFactory httpClientFactory,
    IOptions<MinerUOptions> options,
    ILogger<MinerUPdfConverter> logger) : IPdfStructureConverter
{
    public async Task<PdfStructureDocument> ConvertAsync(string filePath, CancellationToken ct = default)
    {
        var opts = options.Value;
        var fileName = Path.GetFileName(filePath);

        var http = httpClientFactory.CreateClient(nameof(MinerUPdfConverter));

        logger.LogInformation(
            "MinerU conversion started for {FileName} via {ServiceUrl} (backend={Backend}, method={Method})",
            fileName, opts.ServiceUrl, opts.Backend, opts.Method);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        using var form = new MultipartFormDataContent();
        await using var pdfStream = File.OpenRead(filePath);
        var pdfContent = new StreamContent(pdfStream);
        pdfContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
        form.Add(pdfContent, "files", fileName);
        form.Add(new StringContent(opts.Backend), "backend");
        form.Add(new StringContent(opts.Method), "parse_method");
        form.Add(new StringContent("true"), "return_content_list");
        form.Add(new StringContent("false"), "return_md");

        using var resp = await http.PostAsync($"{opts.ServiceUrl.TrimEnd('/')}/file_parse", form, ct);
        resp.EnsureSuccessStatusCode();

        var payload = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        var first = payload.GetProperty("results").EnumerateObject().First().Value;
        var contentListJson = first.GetProperty("content_list").GetString() ?? "[]";
        var blocks = JsonSerializer.Deserialize<List<MinerUBlock>>(contentListJson) ?? [];

        var items = new List<PdfStructureItem>(blocks.Count + 256);
        string? lastHeadingNorm = null;

        // Global set of all section_header norms emitted so far — used for the race-section
        // fallback dedup (bare race title and its " TRAITS" heading are not adjacent, so a
        // consecutive-only check is insufficient).
        var emittedHeadingNorms = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            var text = b.Text?.Trim();
            if (string.IsNullOrEmpty(text)) continue;

            var page = b.PageIdx + 1; // MinerU page_idx is 0-based

            // Spell-chapter recovery: MinerU's layout model rarely tags a spell NAME as a heading
            // (they are run-in bold labels glued to a stat block, not section titles), so spells
            // collapse into body text and never become candidates. Anchor on the rigid spell format:
            // a level/school line (e.g. "3rd-level evocation" / "Conjuration cantrip") immediately
            // followed by a "Casting Time:" block. Promote the spell name (the text before the level
            // digit/school) to a synthetic heading so the scanner yields one candidate per spell.
            if (b.TextLevel is null or 0
                && IsLevelSchoolLine(text)
                && i + 1 < blocks.Count
                && (blocks[i + 1].Text ?? string.Empty).Contains("Casting Time", StringComparison.OrdinalIgnoreCase))
            {
                var name = StripLevelSchool(text);
                if (name.Length == 0 && i > 0)
                    name = StripLevelSchool(blocks[i - 1].Text ?? string.Empty);

                var nameNorm = Normalize(name);
                if (name.Length is >= 2 and <= 40 && nameNorm.Length > 0 && nameNorm != lastHeadingNorm)
                {
                    items.Add(new PdfStructureItem("section_header", name, page, 2));
                    lastHeadingNorm = nameNorm;
                    emittedHeadingNorms.Add(nameNorm);
                }
            }

            if (b.TextLevel is > 0)
            {
                string emitText;

                // FIX 2: Race-section fallback — a short heading ending with " TRAITS" often means
                // the race name was never emitted as its own heading.
                //
                // RENAME strategy: when "GNOME TRAITS" is seen and "GNOME" has NOT yet been emitted,
                // emit the heading as "GNOME" (in place of "GNOME TRAITS") so the section captures
                // the traits body that follows. Do NOT also emit a "GNOME TRAITS" heading — that
                // caused the old INSERT approach to leave the synthetic section empty.
                //
                // When the bare race name WAS already emitted earlier (e.g. "DWARF" heading at the
                // top of its section), emit "DWARF TRAITS" unchanged so it becomes a subsection.
                //
                // Global dedup via emittedHeadingNorms is required because the bare race title and
                // its " TRAITS" heading are typically separated by description paragraphs and a
                // "X NAMES" heading — a consecutive lastHeadingNorm check would miss this.
                if (text.Length <= 40 && text.EndsWith(" TRAITS", StringComparison.OrdinalIgnoreCase))
                {
                    var raceName = text[..(text.Length - " TRAITS".Length)].Trim();
                    var raceNorm = Normalize(raceName);
                    if (raceNorm.Length > 0 && !emittedHeadingNorms.Contains(raceNorm))
                    {
                        // Rename: emit race name in place of "X TRAITS"
                        emitText = raceName;
                        items.Add(new PdfStructureItem("section_header", emitText, page, b.TextLevel));
                        lastHeadingNorm = raceNorm;
                        emittedHeadingNorms.Add(raceNorm);
                        continue; // skip the "GNOME TRAITS" heading — body follows the renamed section
                    }
                    // Race name already emitted → emit "DWARF TRAITS" unchanged (subsection)
                    emitText = text;
                }
                else
                {
                    // FIX 1: Spell-name heading clean — some OCR/layout engines merge the spell name
                    // and its level/school suffix into a single heading block (e.g. "PRESTIDIGITATIONTransmutation cantrip").
                    // When that is the case, emit only the name prefix stripped of the level/school token.
                    emitText = text;
                    if (IsLevelSchoolLine(text))
                    {
                        var stripped = StripLevelSchool(text);
                        if (stripped.Length > 0 && stripped.Length < text.Length)
                            emitText = stripped;
                    }
                }

                items.Add(new PdfStructureItem("section_header", emitText, page, b.TextLevel));
                lastHeadingNorm = Normalize(emitText);
                emittedHeadingNorms.Add(lastHeadingNorm);
            }
            else if (string.Equals(b.Type, "text", StringComparison.OrdinalIgnoreCase))
            {
                items.Add(new PdfStructureItem("text", text, page, null));
            }
            // image / table / header / footer / page_number / equation are intentionally dropped
        }

        logger.LogInformation(
            "MinerU converted {FileName} in {Elapsed:F1}s: {Items} items ({Headings} headings)",
            fileName, sw.Elapsed.TotalSeconds, items.Count, items.Count(i => i.Type == "section_header"));

        return new PdfStructureDocument(string.Empty, items);
    }

    // --- spell-entry recovery (the spell-chapter post-processor) ---

    private static readonly Regex SchoolRx = new(
        "abjurati[ao]n|conjuration|divination|enchantment|evocation|illusion|necromancy|transmutation",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // a digit followed within a few (possibly OCR-garbled) chars by 'leve' — tolerates
    // "3rd-level", "1st leveI", "2nd.level", "3rdievel", etc.
    private static readonly Regex LevelRx = new(@"\d.{0,4}leve", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CantripRx = new(@"\bca[l]*[nl]trip\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DigitRx = new(@"\d", RegexOptions.Compiled);

    private static readonly Regex OcrLevelWordRx = new(@"\b(ca[l]*[nl]trip|lst|ist)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>True if the line looks like a spell's "level &amp; school" line (short; carries a level or school token).</summary>
    private static bool IsLevelSchoolLine(string text) =>
        text.Length <= 90 && (LevelRx.IsMatch(text) || SchoolRx.IsMatch(text) || CantripRx.IsMatch(text));

    /// <summary>
    /// Extract a spell name by cutting at the first level/school marker. Spell names contain no digits,
    /// so cutting at the first digit strips any OCR variant of the level token; the school word and
    /// "cantrip"/"1st"-OCR are also cut points.
    /// </summary>
    private static string StripLevelSchool(string text)
    {
        // Rule 1: if a digit is present the suffix is "<Nth>-level <school>" — cut at the first digit.
        // Spell names never contain digits, so this is unambiguous.
        var d = DigitRx.Match(text);
        if (d.Success)
        {
            var cut = d.Index;
            // OCR artefacts ("lst", "ist") can appear instead of a digit; keep taking the minimum.
            var o = OcrLevelWordRx.Match(text);
            if (o.Success) cut = Math.Min(cut, o.Index);
            return text[..cut].Trim();
        }

        // Rule 2 (cantrip): the suffix is exactly "<school> cantrip" (possibly glued: no space).
        // Find the school word that is IMMEDIATELY followed (optional whitespace) by the cantrip token
        // rather than the first school word in the string (which may be part of the spell name).
        var c = CantripRx.Match(text);
        if (c.Success)
        {
            // Examine only the text before the cantrip token.
            var prefix = text[..c.Index];
            // Walk school matches in reverse; pick the last one whose end abuts the cantrip.
                        foreach (Match sm in SchoolRx.Matches(prefix).Cast<Match>().Reverse())
            {
                var between = prefix[(sm.Index + sm.Length)..];
                if (between.Trim().Length == 0)
                    return text[..sm.Index].Trim();
            }
            // Cantrip found but no school directly before it — fall through to OCR artefact check.
        }

        // Rule 3: OCR-mangled level words ("lst", "ist") with no digit — cut there.
        var ocr = OcrLevelWordRx.Match(text);
        if (ocr.Success)
            return text[..ocr.Index].Trim();

        return text.Trim();
    }

    private static string Normalize(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var ch in s)
            if (char.IsLetterOrDigit(ch))
                sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }

    private sealed record MinerUBlock(
        [property: JsonPropertyName("type")] string? Type,
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("text_level")] int? TextLevel,
        [property: JsonPropertyName("page_idx")] int PageIdx);
}
