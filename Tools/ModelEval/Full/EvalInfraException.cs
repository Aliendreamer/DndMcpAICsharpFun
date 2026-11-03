namespace DndMcpAICsharpFun.Tools.ModelEval.Full;

/// <summary>The eval infrastructure (host, key, test user) is unusable; the whole run must abort.</summary>
internal sealed class EvalInfraException : Exception
{
    public EvalInfraException(string message, Exception? inner = null) : base(message, inner) { }
}
