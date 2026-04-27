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

    public async Task SavePageAsync(int bookId, int pageNumber, IReadOnlyList<ExtractedEntity> entities, CancellationToken ct = default)
    {
        var dir = ExtractedDir(bookId);
        Directory.CreateDirectory(dir);

        var array = new JsonArray();
        foreach (var e in entities)
        {
            array.Add(new JsonObject
            {
                ["page"]        = e.Page,
                ["source_book"] = e.SourceBook,
                ["version"]     = e.Version,
                ["partial"]     = e.Partial,
                ["type"]        = e.Type,
                ["name"]        = e.Name,
                ["data"]        = JsonNode.Parse(e.Data.ToJsonString())
            });
        }

        var path = PageFile(dir, pageNumber);
        await File.WriteAllTextAsync(path, array.ToJsonString(JsonOpts), ct);
    }

    public async Task<IReadOnlyList<IReadOnlyList<ExtractedEntity>>> LoadAllPagesAsync(int bookId, CancellationToken ct = default)
    {
        var dir = ExtractedDir(bookId);
        if (!Directory.Exists(dir))
            return [];

        var files = Directory.GetFiles(dir, "page_*.json")
            .OrderBy(ExtractPageNumber)
            .ToList();

        var result = new List<IReadOnlyList<ExtractedEntity>>();
        foreach (var file in files)
        {
            var json = await File.ReadAllTextAsync(file, ct);
            result.Add(ParsePageFile(json));
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

        // Load all pages as mutable lists
        var pages = new List<List<ExtractedEntity>>();
        foreach (var file in files)
        {
            var json = await File.ReadAllTextAsync(file, ct);
            pages.Add([.. ParsePageFile(json)]);
        }

        // Merge: if page[i] has entity X with partial=true and page[i+1] has same type+name, concatenate
        for (int i = 0; i < pages.Count - 1; i++)
        {
            var current = pages[i];
            var next = pages[i + 1];

            foreach (var entity in current.Where(e => e.Partial).ToList())
            {
                var match = next.FirstOrDefault(e =>
                    string.Equals(e.Type, entity.Type, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.Name, entity.Name, StringComparison.OrdinalIgnoreCase));

                if (match is null) continue;

                var thisDesc = entity.Data["description"]?.GetValue<string>() ?? string.Empty;
                var nextDesc = match.Data["description"]?.GetValue<string>() ?? string.Empty;
                var mergedData = JsonNode.Parse(entity.Data.ToJsonString())!.AsObject();
                mergedData["description"] = thisDesc + nextDesc;

                var merged = entity with { Partial = false, Data = mergedData };
                current[current.IndexOf(entity)] = merged;
                next.Remove(match);
            }
        }

        // Persist updated pages back to disk
        for (int i = 0; i < files.Count; i++)
        {
            var pageNumber = ExtractPageNumber(files[i]);
            await SavePageAsync(bookId, pageNumber, pages[i], ct);
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

    private static IReadOnlyList<ExtractedEntity> ParsePageFile(string json)
    {
        var array = JsonNode.Parse(json)?.AsArray();
        if (array is null) return [];

        var result = new List<ExtractedEntity>();
        foreach (var node in array)
        {
            if (node is not JsonObject obj) continue;
            var data = obj["data"]?.AsObject() ?? new JsonObject();
            result.Add(new ExtractedEntity(
                Page:       obj["page"]?.GetValue<int>() ?? 0,
                SourceBook: obj["source_book"]?.GetValue<string>() ?? string.Empty,
                Version:    obj["version"]?.GetValue<string>() ?? string.Empty,
                Partial:    obj["partial"]?.GetValue<bool>() ?? false,
                Type:       obj["type"]?.GetValue<string>() ?? string.Empty,
                Name:       obj["name"]?.GetValue<string>() ?? string.Empty,
                Data:       data));
        }
        return result;
    }

    private static int ExtractPageNumber(string filePath)
    {
        var name = Path.GetFileNameWithoutExtension(filePath); // "page_42"
        return int.TryParse(name.AsSpan(5), out var n) ? n : 0;
    }
}
