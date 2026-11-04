using System.Text.Json.Nodes;

using DndMcpAICsharpFun.Domain.Entities;

namespace DndMcpAICsharpFun.Features.Entities;

/// <summary>Cheap raw-JSON probe: does a canonical file contain programmatic (PDF-verified) entities?</summary>
public static class ProgrammaticCanonicalGuard
{
    public static bool FileHasProgrammatic(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            var doc = JsonNode.Parse(File.ReadAllText(path));
            return doc?["entities"] is JsonArray arr
                && arr.Any(e => e?["dataSource"]?.GetValue<string>() == DataSources.Programmatic);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            return false;
        }
    }
}
