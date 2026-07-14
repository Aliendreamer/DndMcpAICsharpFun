using DndMcpAICsharpFun.Tools.ModelEval;

using Microsoft.Extensions.AI;

var a = EvalArgs.Parse(args);
Console.WriteLine($"ModelEval — model={a.Model} think={(a.ThinkOn ? "on" : "off")} runs={a.Runs} base={a.BaseUrl}");

var client = ModelClientFactory.Build(a);
var messages = new List<ChatMessage> { new(ChatRole.User, "Say hello in exactly three words.") };
var options = ModelClientFactory.BuildOptions(a.ThinkOn, tools: null);

var response = await client.GetResponseAsync(messages, options);
Console.WriteLine("REPLY: " + (response.Text ?? "(none)"));
return 0;
