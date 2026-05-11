using System.Net;
using System.Text;
using DndMcpAICsharpFun.Features.Search;

namespace DndMcpAICsharpFun.Tests.Search;

internal sealed class FakeMessageHandler(string json, HttpStatusCode status = HttpStatusCode.OK)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct) =>
        Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
}

public sealed class SearXNGClientTests
{
    private static SearXNGClient Build(string json, HttpStatusCode status = HttpStatusCode.OK,
        string[]? allowedDomains = null)
    {
        var handler = new FakeMessageHandler(json, status);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://searxng:8080") };
        var opts = Options.Create(new SearXNGOptions
        {
            Url = "http://searxng:8080",
            MaxResults = 5,
            AllowedDomains = allowedDomains ?? ["dndbeyond.com", "5etools.com"]
        });
        return new SearXNGClient(http, opts, NullLogger<SearXNGClient>.Instance);
    }

    private static string MakeJson(params (string title, string url, string content)[] results)
    {
        var items = string.Join(",", results.Select(r =>
            $$"""{"title":"{{r.title}}","url":"{{r.url}}","content":"{{r.content}}"}"""));
        return $$"""{"results":[{{items}}]}""";
    }

    [Fact]
    public async Task SearchAsync_ReturnsMatchingDomainResults()
    {
        var json = MakeJson(
            ("Fireball", "https://dndbeyond.com/spells/fireball", "8d6 fire damage"),
            ("Some Blog", "https://randomblog.com/fireball", "random post"));
        var client = Build(json);

        var results = await client.SearchAsync("fireball", CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("Fireball", results[0].Title);
        Assert.Equal("https://dndbeyond.com/spells/fireball", results[0].Url);
        Assert.Equal("8d6 fire damage", results[0].Snippet);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmpty_WhenNoDomainMatches()
    {
        var json = MakeJson(("Some Blog", "https://randomblog.com/fireball", "text"));
        var client = Build(json);

        var results = await client.SearchAsync("fireball", CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_ReturnsEmpty_OnHttpFailure()
    {
        var client = Build("{}", HttpStatusCode.ServiceUnavailable);

        var results = await client.SearchAsync("fireball", CancellationToken.None);

        Assert.Empty(results);
    }
}
