using System.Text.Json;
using System.Text.Json.Nodes;
using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Infrastructure.Sqlite;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Ingestion.Extraction;

public sealed class EntityJsonStore(IOptions<IngestionOptions> options) : IEntityJsonStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private string ExtractedDir(int bookId) =>
        Path.Combine(options.Value.BooksPath, "extracted", bookId.ToString());

    private static string PageFile(string dir, int pageNumber) =>
        Path.Combine(dir, $"page_{pageNumber}.json");

    public async Task SavePageAsync(
        int bookId, StructuredPage page, IReadOnlyList<ExtractedEntity> entities,
        CancellationToken ct = default)
    {
        var dir = ExtractedDir(bookId);
        Directory.CreateDirectory(dir);

        var blocksArray = new JsonArray();
        foreach (var b in page.Blocks)
            blocksArray.Add(new JsonObject
            {
                ["order"] = b.Order,
                ["level"] = b.Level,
                ["text"]  = b.Text
            });

        var entitiesArray = new JsonArray();
        foreach (var e in entities)
        {
            var node = new JsonObject
            {
                ["page"]        = e.Page,
                ["source_book"] = e.SourceBook,
                ["version"]     = e.Version,
                ["partial"]     = e.Partial,
                ["type"]        = e.Type,
                ["name"]        = e.Name,
                ["data"]        = JsonNode.Parse(e.Data.ToJsonString())
            };
            if (e.PageEnd.HasValue)
                node["page_end"] = e.PageEnd.Value;
            if (e.SectionTitle is not null)
                node["section_title"] = e.SectionTitle;
            if (e.SectionStart.HasValue)
                node["section_start"] = e.SectionStart.Value;
            if (e.SectionEnd.HasValue)
                node["section_end"] = e.SectionEnd.Value;
            entitiesArray.Add(node);
        }

        var root = new JsonObject
        {
            ["page"]     = page.PageNumber,
            ["raw_text"] = page.RawText,
            ["blocks"]   = blocksArray,
            ["entities"] = entitiesArray
        };

        await File.WriteAllTextAsync(PageFile(dir, page.PageNumber), root.ToJsonString(JsonOpts), ct);
    }

    public async Task<IReadOnlyList<PageData>> LoadAllPagesAsync(int bookId, CancellationToken ct = default)
    {
        var dir = ExtractedDir(bookId);
        if (!Directory.Exists(dir)) return [];

        var files = Directory.GetFiles(dir, "page_*.json")
            .OrderBy(ExtractPageNumber)
            .ToList();

        var result = new List<PageData>(files.Count);
        foreach (var file in files)
        {
            var json = await File.ReadAllTextAsync(file, ct);
            result.Add(ParsePageFile(json, ExtractPageNumber(file)));
        }
        return result;
    }

    public async Task RunMergePassAsync(int bookId, CancellationToken ct = default)
    {
        var dir = ExtractedDir(bookId);
        if (!Directory.Exists(dir)) return;

        var files = Directory.GetFiles(dir, "page_*.json")
            .OrderBy(ExtractPageNumber)
            .ToList();

        if (files.Count < 2) return;

        var pages = new List<(StructuredPage Page, List<ExtractedEntity> Entities)>();
        foreach (var file in files)
        {
            var json = await File.ReadAllTextAsync(file, ct);
            var data = ParsePageFile(json, ExtractPageNumber(file));
            pages.Add((new StructuredPage(data.PageNumber, data.RawText, data.Blocks), [.. data.Entities]));
        }

        for (int i = 0; i < pages.Count - 1; i++)
        {
            var (_, current) = pages[i];
            var (_, next) = pages[i + 1];

            foreach (var entity in current.Where(static e => e.Partial).ToList())
            {
                var match = next.FirstOrDefault(e =>
                    string.Equals(e.Type, entity.Type, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.Name, entity.Name, StringComparison.OrdinalIgnoreCase));

                if (match is null) continue;

                var thisDesc = entity.Data["description"]?.GetValue<string>() ?? string.Empty;
                var nextDesc = match.Data["description"]?.GetValue<string>() ?? string.Empty;
                var mergedData = JsonNode.Parse(entity.Data.ToJsonString())!.AsObject();
                mergedData["description"] = thisDesc + nextDesc;

                var pageEnd = match.PageEnd ?? match.Page;
                var merged = entity with { Partial = false, Data = mergedData, PageEnd = pageEnd };
                current[current.IndexOf(entity)] = merged;
                next.Remove(match);
            }
        }

        for (int i = 0; i < files.Count; i++)
        {
            var (page, entities) = pages[i];
            await SavePageAsync(bookId, page, entities, ct);
        }
    }

    public IEnumerable<string> ListPageFiles(int bookId)
    {
        var dir = ExtractedDir(bookId);
        return Directory.Exists(dir)
            ? Directory.GetFiles(dir, "page_*.json").OrderBy(ExtractPageNumber)
            : [];
    }

    public void DeleteAllPages(int bookId)
    {
        var dir = ExtractedDir(bookId);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    private static PageData ParsePageFile(string json, int fallbackPageNumber)
    {
        var root = JsonNode.Parse(json);

        // New enriched format: { page, raw_text, blocks, entities }
        if (root is JsonObject obj && obj.ContainsKey("entities"))
        {
            var pageNumber = obj["page"]?.GetValue<int>() ?? fallbackPageNumber;
            var rawText    = obj["raw_text"]?.GetValue<string>() ?? string.Empty;
            var blocks     = ParseBlocks(obj["blocks"]?.AsArray());
            var entities   = ParseEntities(obj["entities"]?.AsArray());
            return new PageData(pageNumber, rawText, blocks, entities);
        }

        // Old bare-array format — return empty entities to avoid data corruption
        return new PageData(fallbackPageNumber, string.Empty, [], []);
    }

    private static IReadOnlyList<PageBlock> ParseBlocks(JsonArray? arr)
    {
        if (arr is null) return [];
        var result = new List<PageBlock>(arr.Count);
        foreach (var node in arr)
        {
            if (node is not JsonObject o) continue;
            result.Add(new PageBlock(
                o["order"]?.GetValue<int>() ?? 0,
                o["level"]?.GetValue<string>() ?? "body",
                o["text"]?.GetValue<string>()  ?? string.Empty));
        }
        return result;
    }

    private static IReadOnlyList<ExtractedEntity> ParseEntities(JsonArray? arr)
    {
        if (arr is null) return [];
        var result = new List<ExtractedEntity>(arr.Count);
        foreach (var node in arr)
        {
            if (node is not JsonObject o) continue;
            var data    = o["data"]?.AsObject() ?? new JsonObject();
            var pageEnd      = o.ContainsKey("page_end")      ? o["page_end"]?.GetValue<int>()      : null;
            var sectionStart = o.ContainsKey("section_start") ? o["section_start"]?.GetValue<int>() : null;
            var sectionEnd   = o.ContainsKey("section_end")   ? o["section_end"]?.GetValue<int>()   : null;
            result.Add(new ExtractedEntity(
                Page:         o["page"]?.GetValue<int>()           ?? 0,
                SourceBook:   o["source_book"]?.GetValue<string>() ?? string.Empty,
                Version:      o["version"]?.GetValue<string>()     ?? string.Empty,
                Partial:      o["partial"]?.GetValue<bool>()       ?? false,
                Type:         o["type"]?.GetValue<string>()        ?? string.Empty,
                Name:         o["name"]?.GetValue<string>()        ?? string.Empty,
                Data:         data,
                PageEnd:      pageEnd,
                SectionTitle: o["section_title"]?.GetValue<string>(),
                SectionStart: sectionStart,
                SectionEnd:   sectionEnd));
        }
        return result;
    }

    private static int ExtractPageNumber(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath);
        return int.TryParse(name.AsSpan(5), out var n) ? n : 0;
    }
}
