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
            ValidateFacts(id, "answerFacts", c.AnswerFacts, allowEmptyList: false);
            if (c.ToolDataFacts is not null) ValidateFacts(id, "toolDataFacts", c.ToolDataFacts, allowEmptyList: true);
            result.Add(c with { Tag = "manual", Category = c.Category ?? "manual" });
        }
        return result;
    }

    private static void ValidateFacts(string id, string field, IReadOnlyList<Fact> facts, bool allowEmptyList)
    {
        if (!allowEmptyList && facts.Count == 0)
            throw new InvalidDataException($"Manual case '{id}' has empty '{field}'");
        foreach (var f in facts)
        {
            if (f is null || f.Variants is null || f.Variants.Count == 0)
                throw new InvalidDataException($"Manual case '{id}' has a fact with no variants in '{field}'");
            if (f.Variants.Any(string.IsNullOrWhiteSpace))
                throw new InvalidDataException($"Manual case '{id}' has a null or blank variant in '{field}'");
        }
    }
}
