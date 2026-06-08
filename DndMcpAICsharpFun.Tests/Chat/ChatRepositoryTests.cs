using DndMcpAICsharpFun.Domain;
using DndMcpAICsharpFun.Features.Chat;
using DndMcpAICsharpFun.Tests.Persistence;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Chat;

[Collection("postgres")]
public sealed class ChatRepositoryTests(PostgresFixture pg) : IAsyncLifetime
{
    private readonly ChatRepository _repo = new(new TestDb(pg));

    public Task InitializeAsync() => pg.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Saves_and_replays_turns_in_chronological_order()
    {
        await _repo.AddAsync(new ChatTurn { UserId = 1, Role = "user", Content = "What is fireball?", CreatedAt = DateTime.UtcNow });
        await _repo.AddAsync(new ChatTurn { UserId = 1, Role = "assistant", Content = "A 3rd-level evocation.", CreatedAt = DateTime.UtcNow.AddSeconds(1) });

        var history = await _repo.GetHistoryAsync(1);

        history.Should().HaveCount(2);
        history[0].Role.Should().Be("user");
        history[0].Content.Should().Be("What is fireball?");
        history[1].Role.Should().Be("assistant");
    }

    [Fact]
    public async Task GetHistory_scopes_to_the_user()
    {
        await _repo.AddAsync(new ChatTurn { UserId = 1, Role = "user", Content = "mine", CreatedAt = DateTime.UtcNow });
        await _repo.AddAsync(new ChatTurn { UserId = 2, Role = "user", Content = "theirs", CreatedAt = DateTime.UtcNow });

        (await _repo.GetHistoryAsync(2)).Should().ContainSingle(t => t.Content == "theirs");
    }
}
