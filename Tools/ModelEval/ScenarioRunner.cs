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
        catch (Exception) // MEAI binder throw (e.g. missing required arg) → bind-fail for this run
        {
            sw.Stop();
            // selected may have been captured pre-throw in a fuller impl; v1 records it as a bind failure.
        }

        return new RunResult(selected, bindOk, adhered, sw.ElapsedMilliseconds, firstToolMs);
    }
}
