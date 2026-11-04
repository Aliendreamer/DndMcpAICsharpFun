using System.Net;
using System.Text;
using DndMcpAICsharpFun.Domain.Entities;
using DndMcpAICsharpFun.Features.Ingestion.Programmatic;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Tests.Programmatic;

public class BookExtractorClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Uri, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request.Method, request.RequestUri!.PathAndQuery, body));
            return respond(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string json) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static BookExtractorClient Client(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://extractor:8000") });

    [Fact]
    public async Task Client_parses_result()
    {
        const string result = """
        {"entities":[{"id":"mm14.monster.goblin","type":"Monster","name":"Goblin","page":166,
          "canonicalText":"Goblin text","fields":{"cr":"1/4"},
          "checks":[{"name":"hp-formula","passed":false,"reason":"mismatch"}],
          "provenance":{"pages":[{"page":166,"boxes":[{"x0":1.5,"y0":2.25,"x1":300.0,"y1":400.75}]}]}}],
         "report":{"pages":1}}
        """;
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, result));

        var r = await Client(handler).GetResultAsync("job1", CancellationToken.None);

        handler.Requests.Single().Uri.Should().Be("/jobs/job1/result");
        var e = r.Entities.Single();
        e.Id.Should().Be("mm14.monster.goblin");
        e.Type.Should().Be(EntityType.Monster);
        e.Page.Should().Be(166);
        e.Fields.GetProperty("cr").GetString().Should().Be("1/4");
        e.Checks.Single().Should().Be(new EntityCheck("hp-formula", false, "mismatch"));
        e.Provenance.Pages.Single().Boxes.Single().Should().Be(new PageBox(1.5, 2.25, 300.0, 400.75));
        r.Report.GetProperty("pages").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Client_submit_and_status()
    {
        var handler = new StubHandler(req => req.Method == HttpMethod.Post
            ? Json(HttpStatusCode.Accepted, """{"jobId":"abc"}""")
            : Json(HttpStatusCode.OK, """{"status":"running","pagesDone":3,"pagesTotal":10}"""));
        var client = Client(handler);

        var id = await client.SubmitAsync("mm.pdf", "mm14", CancellationToken.None);
        var status = await client.GetStatusAsync(id, CancellationToken.None);

        id.Should().Be("abc");
        handler.Requests[0].Body.Should().Contain("\"pdfPath\":\"mm.pdf\"").And.Contain("\"bookSlug\":\"mm14\"");
        status.Status.Should().Be("running");
        status.PagesDone.Should().Be(3);
        status.PagesTotal.Should().Be(10);
        status.Error.Should().BeNull();
    }

    [Fact]
    public async Task Client_non_success_throws_HttpRequestException()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.Conflict, "{}"));
        var act = () => Client(handler).SubmitAsync("mm.pdf", "mm14", CancellationToken.None);
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task Client_page_image_returns_bytes_and_escapes_query()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([1, 2, 3]),
        });
        var bytes = await Client(handler).GetPageImageAsync("a b.pdf", 7, CancellationToken.None);
        bytes.Should().Equal(1, 2, 3);
        handler.Requests.Single().Uri.Should().Be("/page-image?pdfPath=a%20b.pdf&page=7");
    }

    [Fact]
    public void Options_defaults()
    {
        var o = new BookExtractorOptions();
        o.ServiceUrl.Should().Be("http://localhost:8000");
        o.PollInterval.Should().Be(TimeSpan.FromSeconds(10));
        o.Timeout.Should().Be(TimeSpan.FromHours(4));
        _ = Options.Create(o);
    }
}
