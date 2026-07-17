using System.Net;
using System.Text;

using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.EntityExtraction.Authority;
using DndMcpAICsharpFun.Features.Search;

using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Tests.Entities.Extraction.Authority;

public sealed class WebAuthorityRefereeTests
{
    // Counts invocations so the "disabled → no web call" and cache tests can assert on network use.
    private sealed class CountingHandler(string json, HttpStatusCode status = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static string Json(params (string title, string url, string content)[] results)
    {
        var items = string.Join(",", results.Select(r =>
            $$"""{"title":"{{r.title}}","url":"{{r.url}}","content":"{{r.content}}"}"""));
        return $$"""{"results":[{{items}}]}""";
    }

    private static (WebAuthorityReferee Referee, CountingHandler Handler) Build(
        string json, bool enabled = true, string[]? authoritativeDomains = null)
    {
        var handler = new CountingHandler(json);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://searxng:8080") };
        // Empty AllowedDomains = the SearXNG client returns every result; the referee then applies
        // its OWN authoritative-domain filter, which is what we exercise here.
        var searxng = new SearXNGClient(
            http,
            Options.Create(new SearXNGOptions { Url = "http://searxng:8080", MaxResults = 10, AllowedDomains = [] }),
            NullLogger<SearXNGClient>.Instance);
        var referee = new WebAuthorityReferee(
            searxng,
            Options.Create(new WebAuthorityRefereeOptions
            {
                Enabled = enabled,
                TimeoutSeconds = 5,
                MinAuthoritativeHits = 1,
                AuthoritativeDomains = authoritativeDomains ?? ["5e.tools", "dnd5e.wikidot.com"],
            }),
            NullLogger<WebAuthorityReferee>.Instance);
        return (referee, handler);
    }

    [Fact]
    public async Task Confirms_when_an_authoritative_domain_hit_names_the_entity()
    {
        var (referee, _) = Build(Json(
            ("Flumph", "https://5e.tools/bestiary.html#flumph", "The flumph is a lawful good aberration"),
            ("Random", "https://someblog.example/flumph", "a homebrew flumph fan post")));

        var verified = await referee.IsAuthoritativeAsync("Flumph", EntityType.Monster, CancellationToken.None);

        verified.Should().BeTrue();
    }

    [Fact]
    public async Task Misses_when_the_only_hits_are_non_authoritative_domains()
    {
        var (referee, _) = Build(Json(
            ("Flumph", "https://someblog.example/flumph", "a homebrew flumph fan post")));

        var verified = await referee.IsAuthoritativeAsync("Flumph", EntityType.Monster, CancellationToken.None);

        verified.Should().BeFalse("a non-authoritative domain must not confirm (refute-bias)");
    }

    [Fact]
    public async Task Misses_when_an_authoritative_hit_does_not_name_the_entity()
    {
        // An authoritative page turns up but is about something else — refute-bias requires the
        // entity's own name to appear, not merely an authoritative URL.
        var (referee, _) = Build(Json(
            ("Beholder", "https://5e.tools/bestiary.html#beholder", "The beholder is an aberration")));

        var verified = await referee.IsAuthoritativeAsync("Grung", EntityType.Monster, CancellationToken.None);

        verified.Should().BeFalse();
    }

    [Fact]
    public async Task Disabled_makes_no_web_call_and_returns_false()
    {
        var (referee, handler) = Build(Json(
            ("Flumph", "https://5e.tools/bestiary.html#flumph", "flumph")), enabled: false);

        var verified = await referee.IsAuthoritativeAsync("Flumph", EntityType.Monster, CancellationToken.None);

        verified.Should().BeFalse();
        handler.Calls.Should().Be(0, "the toggle is off — no web request may be issued");
    }

    [Fact]
    public async Task Caches_the_verdict_by_normalized_name()
    {
        var (referee, handler) = Build(Json(
            ("Flumph", "https://5e.tools/bestiary.html#flumph", "The flumph is an aberration")));

        (await referee.IsAuthoritativeAsync("Flumph", EntityType.Monster, CancellationToken.None)).Should().BeTrue();
        // Punctuation/spacing variant normalizes to the same key → served from cache, no 2nd call.
        (await referee.IsAuthoritativeAsync("  flumph ", EntityType.Monster, CancellationToken.None)).Should().BeTrue();

        handler.Calls.Should().Be(1);
    }
}
