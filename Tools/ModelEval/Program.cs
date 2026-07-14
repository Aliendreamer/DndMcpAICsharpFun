using DndMcpAICsharpFun.Tools.ModelEval;

const string persona =
    "You are a D&D companion assistant. When the user's request matches one of your tools, CALL " +
    "the tool and answer STRICTLY from its result — never fabricate numbers, stat blocks, or citations. " +
    "For casual conversation with no tool match, just reply normally without calling a tool. " +
    "Report tool results in prose, not as numbered lists.";

var a = EvalArgs.Parse(args);
var client = ModelClientFactory.Build(a);

var results = new List<(Scenario, IReadOnlyList<RunResult>)>();
foreach (var scenario in Scenarios.All)
{
    var runs = new List<RunResult>();
    for (var i = 0; i < a.Runs; i++)
    {
        Console.Error.WriteLine($"[{scenario.Name}] run {i + 1}/{a.Runs}...");
        runs.Add(await ScenarioRunner.RunOnceAsync(client, scenario, a.ThinkOn, persona));
    }
    results.Add((scenario, runs));
}

Scorecard.Print(a, results);
return 0;
