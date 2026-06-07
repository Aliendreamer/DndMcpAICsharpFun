using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction;
using DndMcpAICsharpFun.Features.Ingestion.Extraction;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DndMcpAICsharpFun.Tests.Spike;

/// <summary>
/// SPIKE harness — marker-converter-spike (openspec/changes/marker-converter-spike).
/// Compares Marker vs Docling conversion quality on Tasha's Cauldron of Everything by
/// running both converters' items through the real candidate scanner and the
/// needs-review name heuristic. Produces data/spike/marker-vs-docling.md.
/// Requires: RUN_SPIKE=1, marker service on :5002, Tasha PDF + docling cache present.
/// Run: RUN_SPIKE=1 dotnet test --filter "FullyQualifiedName~MarkerVsDocling"
/// </summary>
public sealed class MarkerVsDoclingComparisonTests
{
    private const string RepoRoot = "/home/aliendreamer/projects/DndMcpAICsharpFun";
    private const string PdfHostPath = RepoRoot + "/books/4e8f1fe851c34c7db1e8a0d8e1bff02d.pdf";
    private const string PdfContainerPath = "/books/4e8f1fe851c34c7db1e8a0d8e1bff02d.pdf";
    private const string DoclingCachePath =
        RepoRoot + "/data/docling-cache/8e7550f9c5ba60ec0311f6349ac6a7eb2e902c6078654b4927457202e2d65d97.json";
    private const string MarkerUrl = "http://localhost:5002";
    private const string ReportPath = RepoRoot + "/data/spike/marker-vs-docling.md";

    private static readonly JsonSerializerOptions CacheJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task CompareConverters()
    {
        if (Environment.GetEnvironmentVariable("RUN_SPIKE") != "1")
            return; // manual spike — no-op in normal test runs

        // --- Docling side: read the disk cache directly.
        await using var cacheStream = File.OpenRead(DoclingCachePath);
        var doclingDoc = (await JsonSerializer.DeserializeAsync<PdfStructureDocument>(cacheStream, CacheJsonOptions))!;

        // --- Marker side: full conversion via the spike service (slow, one-time).
        var marker = new MarkerPdfConverter(MarkerUrl, PdfContainerPath);
        var markerDoc = await marker.ConvertAsync(PdfHostPath);

        // --- Shared, converter-independent TOC map from PDF bookmarks.
        var bookmarkReader = new PdfPigBookmarkReader(NullLogger<PdfPigBookmarkReader>.Instance);
        var tocEntries = BookmarkTocMapper.Map(bookmarkReader.ReadBookmarks(PdfHostPath));
        var tocMap = new TocCategoryMap(tocEntries);

        var scanner = new EntityCandidateScanner();
        var docling = Analyze(scanner, tocMap, doclingDoc, "Docling");
        var markerR = Analyze(scanner, tocMap, markerDoc, "Marker");

        WriteReport(docling, markerR);
        Assert.True(File.Exists(ReportPath));
    }

    /// <summary>
    /// Diagnoses the Monster candidate drop (Marker 4 vs Docling 15) using the SAVED
    /// marker result JSON (data/spike/marker-result.json) — no re-conversion.
    /// Run: RUN_SPIKE=1 dotnet test --filter "FullyQualifiedName~MonsterDrop"
    /// </summary>
    [Fact]
    public async Task MonsterDropInvestigation()
    {
        if (Environment.GetEnvironmentVariable("RUN_SPIKE") != "1")
            return;

        await using var cacheStream = File.OpenRead(DoclingCachePath);
        var doclingDoc = (await JsonSerializer.DeserializeAsync<PdfStructureDocument>(cacheStream, CacheJsonOptions))!;

        using var markerJson = JsonDocument.Parse(File.ReadAllText(RepoRoot + "/data/spike/marker-result.json"));
        var markerDoc = MarkerPdfConverter.FromMarkerJson(markerJson.RootElement);

        var bookmarkReader = new PdfPigBookmarkReader(NullLogger<PdfPigBookmarkReader>.Instance);
        var tocEntries = BookmarkTocMapper.Map(bookmarkReader.ReadBookmarks(PdfHostPath));
        var tocMap = new TocCategoryMap(tocEntries);

        // Monster page ranges per the TOC map.
        var maxPage = Math.Max(
            doclingDoc.Items.Max(i => i.PageNumber),
            markerDoc.Items.Max(i => i.PageNumber));
        var monsterPages = Enumerable.Range(1, maxPage)
            .Where(p => tocMap.GetCategory(p) == DndMcpAICsharpFun.Domain.ContentCategory.Monster)
            .ToList();

        var scanner = new EntityCandidateScanner();
        var dCands = scanner.Scan(BuildScannerInputs(doclingDoc.Items), tocMap).ToList();
        var mCands = scanner.Scan(BuildScannerInputs(markerDoc.Items), tocMap).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("# Monster Drop Investigation");
        sb.AppendLine();
        sb.AppendLine($"Monster pages per TOC map: {string.Join(", ", monsterPages)}");
        sb.AppendLine();
        sb.AppendLine("## Monster candidates");
        sb.AppendLine();
        sb.AppendLine("### Docling");
        foreach (var c in dCands.Where(c => c.Type == DndMcpAICsharpFun.Domain.Entities.EntityType.Monster))
            sb.AppendLine($"- p{c.Page}: {c.DisplayName}");
        sb.AppendLine();
        sb.AppendLine("### Marker");
        foreach (var c in mCands.Where(c => c.Type == DndMcpAICsharpFun.Domain.Entities.EntityType.Monster))
            sb.AppendLine($"- p{c.Page}: {c.DisplayName}");
        sb.AppendLine();
        sb.AppendLine("## Headings on Monster pages (side by side)");
        sb.AppendLine();
        foreach (var p in monsterPages)
        {
            var dh = doclingDoc.Items.Where(i => i.PageNumber == p && IsHeading(i)).Select(i => i.Text).ToList();
            var mh = markerDoc.Items.Where(i => i.PageNumber == p && IsHeading(i)).Select(i => i.Text).ToList();
            if (dh.Count == 0 && mh.Count == 0) continue;
            sb.AppendLine($"### Page {p}");
            sb.AppendLine($"- Docling ({dh.Count}): {string.Join(" | ", dh)}");
            sb.AppendLine($"- Marker  ({mh.Count}): {string.Join(" | ", mh)}");
            sb.AppendLine();
        }

        // Page alignment probe: where does each converter see the same heading text?
        sb.AppendLine("## Page alignment probe (shared heading texts, first 15)");
        sb.AppendLine();
        var dByText = doclingDoc.Items.Where(IsHeading).GroupBy(i => Normalize(i.Text)).ToDictionary(g => g.Key, g => g.First().PageNumber);
        var mByText = markerDoc.Items.Where(IsHeading).GroupBy(i => Normalize(i.Text)).ToDictionary(g => g.Key, g => g.First().PageNumber);
        var shared = dByText.Keys.Intersect(mByText.Keys).Take(15);
        sb.AppendLine("| Heading | Docling page | Marker page |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (var t in shared)
            sb.AppendLine($"| {t} | {dByText[t]} | {mByText[t]} |");

        File.WriteAllText(RepoRoot + "/data/spike/monster-drop-investigation.md", sb.ToString());
        Assert.True(true);
    }

    private static bool IsHeading(PdfStructureItem i) =>
        (i.Type ?? "").StartsWith("section", StringComparison.OrdinalIgnoreCase) ||
        (i.Type ?? "").StartsWith("heading", StringComparison.OrdinalIgnoreCase) ||
        (i.Type ?? "").Equals("title", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string s) => s.Trim().ToUpperInvariant();

    private sealed record ConverterResult(
        string Name,
        int ItemCount,
        int HeadingCount,
        IReadOnlyList<EntityCandidate> Candidates,
        IReadOnlyList<EntityCandidate> Flagged);

    private static ConverterResult Analyze(
        EntityCandidateScanner scanner, TocCategoryMap tocMap, PdfStructureDocument doc, string name)
    {
        var inputs = BuildScannerInputs(doc.Items);
        var candidates = scanner.Scan(inputs, tocMap).ToList();
        var flagged = candidates
            .Where(c => ExtractionNeedsReview.Derive(c.DisplayName, confidence: null))
            .ToList();
        var headings = doc.Items.Count(i =>
            (i.Type ?? "").StartsWith("section", StringComparison.OrdinalIgnoreCase) ||
            (i.Type ?? "").StartsWith("heading", StringComparison.OrdinalIgnoreCase) ||
            (i.Type ?? "").Equals("title", StringComparison.OrdinalIgnoreCase));
        return new ConverterResult(name, doc.Items.Count, headings, candidates, flagged);
    }

    // Replicates EntityExtractionOrchestrator.BuildScannerInputs (private) — spike duplication.
    private static IList<ScannerInput> BuildScannerInputs(IReadOnlyList<PdfStructureItem> items)
    {
        var inputs = new List<ScannerInput>(items.Count);
        var currentSection = "(unknown)";
        foreach (var item in items)
        {
            var type = item.Type ?? string.Empty;
            if (type.StartsWith("section", StringComparison.OrdinalIgnoreCase) ||
                type.StartsWith("heading", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("title", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(item.Text))
                    currentSection = item.Text.Trim();
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.Text)) continue;
            inputs.Add(new ScannerInput(currentSection, item.PageNumber, item.Text));
        }
        return inputs;
    }

    private static void WriteReport(ConverterResult docling, ConverterResult marker)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Marker vs Docling — Conversion Quality Report (Tasha's Cauldron of Everything)");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine("## Headline");
        sb.AppendLine();
        sb.AppendLine("| Metric | Docling | Marker |");
        sb.AppendLine("| --- | --- | --- |");
        sb.AppendLine($"| Structured items | {docling.ItemCount} | {marker.ItemCount} |");
        sb.AppendLine($"| Heading items | {docling.HeadingCount} | {marker.HeadingCount} |");
        sb.AppendLine($"| Entity candidates | {docling.Candidates.Count} | {marker.Candidates.Count} |");
        sb.AppendLine($"| Flagged (garbled) names | {docling.Flagged.Count} | {marker.Flagged.Count} |");
        sb.AppendLine($"| Flagged rate | {Rate(docling)} | {Rate(marker)} |");
        sb.AppendLine();

        sb.AppendLine("## Candidates per entity type");
        sb.AppendLine();
        sb.AppendLine("| Type | Docling | Marker |");
        sb.AppendLine("| --- | --- | --- |");
        var types = docling.Candidates.Select(c => c.Type)
            .Concat(marker.Candidates.Select(c => c.Type)).Distinct().OrderBy(t => t.ToString());
        foreach (var t in types)
        {
            sb.AppendLine(
                $"| {t} | {docling.Candidates.Count(c => c.Type == t)} | {marker.Candidates.Count(c => c.Type == t)} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Sample: flagged Docling names vs Marker names on the same page");
        sb.AppendLine();
        sb.AppendLine("| Page | Docling name (flagged) | Marker name on same page |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (var d in docling.Flagged.Take(25))
        {
            var m = marker.Candidates.FirstOrDefault(c => c.Page == d.Page);
            var mName = m is null ? "(none)" : m.DisplayName;
            var mFlag = m is not null && ExtractionNeedsReview.Derive(m.DisplayName, null) ? " ⚠" : "";
            sb.AppendLine($"| {d.Page} | {Escape(d.DisplayName)} | {Escape(mName)}{mFlag} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Marker-only flagged names (first 15)");
        sb.AppendLine();
        foreach (var m in marker.Flagged.Take(15))
            sb.AppendLine($"- p{m.Page}: {Escape(m.DisplayName)}");
        sb.AppendLine();

        sb.AppendLine("## Verdict inputs");
        sb.AppendLine();
        sb.AppendLine($"- Docling flagged rate: **{Rate(docling)}** — current pipeline baseline");
        sb.AppendLine($"- Marker flagged rate: **{Rate(marker)}**");
        sb.AppendLine("- Decision rule (design.md): Marker ≲15% and tables no worse → spec real migration; comparable/worse → delete spike.");

        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath)!);
        File.WriteAllText(ReportPath, sb.ToString());
    }

    private static string Rate(ConverterResult r) =>
        r.Candidates.Count == 0 ? "n/a" : $"{100.0 * r.Flagged.Count / r.Candidates.Count:F1}%";

    private static string Escape(string s) => s.Replace("|", "\\|");
}
