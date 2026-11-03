using DndMcpAICsharpFun.Features.Chat;

using FluentAssertions;

using Microsoft.Extensions.AI;

namespace DndMcpAICsharpFun.Tests.Chat;

public sealed class ChatTurnTraceTests
{
    private static ChatResponse Response(params ChatMessage[] messages) => new(messages.ToList());

    private static ChatMessage Call(string callId, string name, IDictionary<string, object?>? args = null) =>
        new(ChatRole.Assistant, [new FunctionCallContent(callId, name, args)]);

    private static ChatMessage Result(string callId, object? result, Exception? ex = null) =>
        new(ChatRole.Tool, [new FunctionResultContent(callId, result) { Exception = ex }]);

    [Fact]
    public void Build_PairsCallWithResultByCallId()
    {
        var response = Response(
            Call("c1", "get_entity", new Dictionary<string, object?> { ["id"] = "phb14.spell.fireball" }),
            Result("c1", "Fireball text"));

        var traces = ChatTurnTraceBuilder.Build(response);

        traces.Should().ContainSingle();
        traces[0].Name.Should().Be("get_entity");
        traces[0].ArgumentsJson.Should().Contain("phb14.spell.fireball");
        traces[0].Result.Should().Be("Fireball text");
        traces[0].Error.Should().BeNull();
    }

    [Fact]
    public void Build_SerializesObjectResultsAsJson()
    {
        var response = Response(Call("c1", "calculate_crafting"), Result("c1", new { materialsGp = 750 }));

        var traces = ChatTurnTraceBuilder.Build(response);

        traces[0].Result.Should().Contain("\"materialsGp\":750");
        traces[0].Error.Should().BeNull();
    }

    [Fact]
    public void Build_ExceptionResult_SetsError()
    {
        var response = Response(
            Call("c1", "calculate_crafting"),
            Result("c1", null, new InvalidOperationException("missing required parameter")));

        var traces = ChatTurnTraceBuilder.Build(response);

        traces[0].Error.Should().Contain("missing required parameter");
    }

    [Fact]
    public void Build_ErrorObjectOrErrorText_SetsError()
    {
        var response = Response(
            Call("c1", "calculate_crafting"), Result("c1", new { error = "Unknown rarity" }),
            Call("c2", "get_entity"), Result("c2", "Error: boom"));

        var traces = ChatTurnTraceBuilder.Build(response);

        traces[0].Error.Should().Contain("Unknown rarity");
        traces[1].Error.Should().Contain("boom");
    }

    [Fact]
    public void Build_MultipleCalls_PreservesOrder()
    {
        var response = Response(
            Call("a", "first"), Result("a", "1"),
            Call("b", "second"), Result("b", "2"),
            Call("c", "third"));

        var traces = ChatTurnTraceBuilder.Build(response);

        traces.Select(t => t.Name).Should().Equal("first", "second", "third");
        traces[2].Result.Should().BeNull();
    }

    [Fact]
    public void Build_McpCallToolResult_UnwrapsContentText()
    {
        var el = System.Text.Json.JsonDocument.Parse(
            "{\"content\":[{\"type\":\"text\",\"text\":\"{\\\"hd\\\":{\\\"faces\\\":6}}\"}],\"isError\":false}").RootElement.Clone();
        var traces = ChatTurnTraceBuilder.Build(Response(Call("c1", "t"), Result("c1", el)));

        traces[0].Result.Should().Contain("\"faces\":6");
        traces[0].Error.Should().BeNull();
    }

    [Fact]
    public void Build_McpIsError_SetsError()
    {
        var el = System.Text.Json.JsonDocument.Parse(
            "{\"content\":[{\"type\":\"text\",\"text\":\"bad rarity\"}],\"isError\":true}").RootElement.Clone();
        var empty = System.Text.Json.JsonDocument.Parse("{\"content\":[],\"isError\":true}").RootElement.Clone();
        var traces = ChatTurnTraceBuilder.Build(Response(
            Call("a", "t"), Result("a", el), Call("b", "t"), Result("b", empty)));

        traces[0].Error.Should().Be("bad rarity");
        traces[1].Error.Should().Be("MCP tool error");
    }

    [Fact]
    public void Build_JsonElementStringResult_IsUnwrapped()
    {
        var el = System.Text.Json.JsonDocument.Parse("\"plain text\"").RootElement.Clone();
        var traces = ChatTurnTraceBuilder.Build(Response(Call("c1", "t"), Result("c1", el)));

        traces[0].Result.Should().Be("plain text");
        traces[0].Error.Should().BeNull();
    }

    [Fact]
    public void Build_BlankErrorProperty_StillErrors()
    {
        var traces = ChatTurnTraceBuilder.Build(Response(Call("c1", "t"), Result("c1", new { error = " " })));

        traces[0].Error.Should().Be("error");
    }
}
