using System.Text.Json;

namespace DndMcpAICsharpFun.Tools.ModelEval.Full;

internal static class ManualCases
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Loads hand-written cases; returns empty if the file is missing. Forces Tag = "manual".</summary>
    public static IReadOnlyList<EvalCase> Load(string path)
    {
        if (!File.Exists(path)) return [];

        var raw = JsonSerializer.Deserialize<List<EvalCase?>>(File.ReadAllText(path), Options) ?? [];
        var result = new List<EvalCase>(raw.Count);
        for (var i = 0; i < raw.Count; i++)
        {
            var c = raw[i] ?? throw new InvalidDataException($"Manual case #{i} is null");
            var id = c.Id;
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidDataException($"Manual case #{i} is missing 'id'");
            if (string.IsNullOrWhiteSpace(c.Prompt)) throw new InvalidDataException($"Manual case '{id}' is missing 'prompt'");
            if (c.ExpectedTools is null) throw new InvalidDataException($"Manual case '{id}' is missing 'expectedTools'");
            if (c.AnswerFacts is null) throw new InvalidDataException($"Manual case '{id}' is missing 'answerFacts'");
            result.Add(c with { Tag = "manual", Category = c.Category ?? "manual" });
        }
        return result;
    }
}
