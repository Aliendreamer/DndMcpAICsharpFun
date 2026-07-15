using DndMcpAICsharpFun.Tools.ModelEval;

using FluentAssertions;

using Microsoft.Extensions.AI;

namespace DndMcpAICsharpFun.Tests.ModelEval;

public class StubToolBindingTests
{
    public static IEnumerable<object[]> OptionalStubParams() =>
    [
        ["ask_rules", "ruleTopics"],
        ["ask_rules", "edition"],
        ["build_encounter", "campaignId"],
        ["build_encounter", "theme"],
        ["build_encounter", "maxCr"],
        ["build_encounter", "minCr"],
        ["plan_downtime", "edition"],
        ["plan_level_up", "targetClass"],
        ["plan_level_up", "considerDip"],
        ["generate_npc", "maxCr"],
        ["ask_setting_lore", "edition"],
    ];

    [Theory]
    [MemberData(nameof(OptionalStubParams))]
    public void Optional_stub_tool_param_is_not_in_the_schema_required_set(string toolName, string paramName)
    {
        // AIFunctionFactory marks a parameter required unless it has a C# default value; a nullable
        // type is NOT enough. These stub params mirror the real chat tools' documented-optional
        // params, so the model must be able to omit them — i.e. they must be absent from the tool
        // schema's `required` array. Without a `= null` default, MEAI's binder throws
        // "missing required parameter" before the stub delegate ever runs.
        var tool = StubTools.Build().OfType<AIFunction>().Single(t => t.Name == toolName);

        var required = tool.JsonSchema.TryGetProperty("required", out var req)
            ? req.EnumerateArray().Select(e => e.GetString()).ToArray()
            : Array.Empty<string?>();

        required.Should().NotContain(paramName,
            $"{toolName}.{paramName} is optional and the model must be able to omit it");
    }
}