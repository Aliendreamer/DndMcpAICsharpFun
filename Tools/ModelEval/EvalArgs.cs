namespace DndMcpAICsharpFun.Tools.ModelEval;

internal sealed record EvalArgs(string Model, bool ThinkOn, int Runs, string BaseUrl)
{
    public static EvalArgs Parse(string[] args)
    {
        var model = "qwen3:8b";
        var thinkOn = true;
        var runs = 5;
        var baseUrl = "http://localhost:11434";

        for (var i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--model": model = args[++i]; break;
                case "--think": thinkOn = !string.Equals(args[++i], "off", StringComparison.OrdinalIgnoreCase); break;
                case "--runs": runs = int.Parse(args[++i]); break;
                case "--base-url": baseUrl = args[++i]; break;
            }
        }

        return new EvalArgs(model, thinkOn, runs, baseUrl);
    }
}
