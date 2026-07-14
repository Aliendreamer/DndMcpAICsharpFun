using System.Diagnostics;

using Microsoft.Extensions.AI;

namespace DndMcpAICsharpFun.Tools.ModelEval;

internal static class ScenarioRunner
{
    public static async Task<RunResult> RunOnceAsync(IChatClient client, Scenario s, bool thinkOn, string persona)
    {
        StubState.Reset();
        StubState.AskRulesReturnsEmpty = s.StubEmpty;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, persona),
            new(ChatRole.User, s.Prompt),
        };
        var options = ModelClientFactory.BuildOptions(thinkOn, StubTools.Build());

        var sw = Stopwatch.StartNew();
        string? selected = null;
        var bindOk = false;
        var adhered = false;
        long? firstToolMs = null;

        try
        {
            var response = await client.GetResponseAsync(messages, options);
            sw.Stop();

            // Selection: the model's chosen tool, read pre-binding from the round-trip messages.
            // Known limitation (v1): FirstOrDefault anchors to the FIRST call/result only. If the model
            // fails a bind then self-corrects and retries successfully in the same round-trip, this run
            // is still scored bind-fail — a successful mid-loop retry is not credited.
            var call = response.Messages
                .SelectMany(m => m.Contents)
                .OfType<FunctionCallContent>()
                .FirstOrDefault();
            selected = call?.Name;

            // Binding: a result with no Exception AND the stub delegate recorded the invocation.
            var result = response.Messages
                .SelectMany(m => m.Contents)
                .OfType<FunctionResultContent>()
                .FirstOrDefault(r => r.CallId == call?.CallId);
            bindOk = selected is not null
                && result?.Exception is null
                && StubState.InvokedTools.Contains(selected);

            if (selected is not null)
                firstToolMs = sw.ElapsedMilliseconds; // whole round-trip incl. tool call; finer split not needed v1

            adhered = s.ExpectedTool is null
                ? selected is null                                  // negative case: adhered == picked no tool
                : selected == s.ExpectedTool && s.AdherenceCheck(response.Text ?? string.Empty);
        }
        catch (Exception ex) // MEAI binder throw (e.g. missing required arg) → bind-fail for this run
        {
            sw.Stop();
            // A throw here is scored as a bind failure. Surface it on stderr so an operator can tell a
            // genuine model/binder failure from infra trouble (Ollama down, timeout) — the scorecard
            // (stdout) cannot distinguish them.
            Console.Error.WriteLine($"[{s.Name}] exception (scored bind-fail): {ex.Message}");
        }

        return new RunResult(selected, bindOk, adhered, sw.ElapsedMilliseconds, firstToolMs);
    }
}
