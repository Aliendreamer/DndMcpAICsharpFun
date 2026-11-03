using DndMcpAICsharpFun.Features.Auth;
using DndMcpAICsharpFun.Features.Chat;

using DndMcpAICsharpFun.Infrastructure.Ollama;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Features.Admin;

public sealed record EvalTurnRequest(string Prompt, long? UserId = null, bool AllowWebSearch = false);

public sealed record EvalTurnResponse(
    string Model,
    string Text,
    IReadOnlyList<string> OfferedTools,
    IReadOnlyList<ToolCallTrace> ToolCalls,
    long LatencyMs);

/// <summary>Seam over <see cref="DndChatService.RunTurnAsync"/> so the endpoint is testable without the chat stack.</summary>
public interface IEvalTurnRunner
{
    Task<ChatTurnResult> RunAsync(string prompt, long userId, bool allowWebSearch, CancellationToken ct);
}

public sealed class DndChatEvalTurnRunner(DndChatService chat) : IEvalTurnRunner
{
    public Task<ChatTurnResult> RunAsync(string prompt, long userId, bool allowWebSearch, CancellationToken ct) =>
        chat.RunTurnAsync([new ChatMessage(ChatRole.User, prompt)], userId, allowWebSearch, ct);
}

/// <summary>Resolves the seeded <c>test</c> user's id (null when absent).</summary>
public interface IEvalUserResolver
{
    Task<long?> ResolveTestUserAsync(CancellationToken ct);
}

public sealed class RepositoryEvalUserResolver(UserRepository users) : IEvalUserResolver
{
    public async Task<long?> ResolveTestUserAsync(CancellationToken ct) =>
        (await users.FindByUsernameAsync("test"))?.Id;
}

public static class EvalAdminEndpoints
{
    public static RouteGroupBuilder MapEvalAdmin(this RouteGroupBuilder group)
    {
        // POST /admin/eval/turn — one isolated production chat turn; returns text + tool trace.
        group.MapPost("/eval/turn", async (
            EvalTurnRequest? body,
            IEvalTurnRunner runner,
            IEvalUserResolver users,
            IOptions<OllamaOptions> ollama,
            CancellationToken ct) =>
        {
            if (body is null || string.IsNullOrWhiteSpace(body.Prompt))
                return Results.Problem("prompt is required.", statusCode: StatusCodes.Status400BadRequest);

            var userId = body.UserId;
            if (userId is null)
            {
                userId = await users.ResolveTestUserAsync(ct);
                if (userId is null)
                    return Results.Problem("No userId supplied and the seeded 'test' user was not found.",
                        statusCode: StatusCodes.Status400BadRequest);
            }

            ChatTurnResult turn;
            try
            {
                turn = await runner.RunAsync(body.Prompt, userId.Value, body.AllowWebSearch, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Only the caller aborting propagates; an HttpClient timeout (also an
                // OperationCanceledException) is a model/infra failure -> 502 below.
                throw;
            }
            catch (Exception ex)
            {
                return Results.Problem($"Chat turn failed: {ex.Message}", statusCode: StatusCodes.Status502BadGateway);
            }

            return Results.Ok(new EvalTurnResponse(
                ollama.Value.ChatModel,
                turn.Text, turn.OfferedTools, turn.ToolCalls, turn.LatencyMs));
        });

        return group;
    }
}