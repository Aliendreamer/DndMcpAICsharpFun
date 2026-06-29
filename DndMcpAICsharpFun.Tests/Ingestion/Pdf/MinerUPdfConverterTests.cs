using System.Net;
using System.Text;
using System.Text.Json;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Tests.Ingestion.Pdf;

public class MinerUPdfConverterTests
{
    /// <summary>
    /// Captures the outgoing request and returns a canned <c>/file_parse</c> response.
    /// </summary>
    private sealed class StubHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? Captured { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Captured = request;
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Builds a MinerU <c>/file_parse</c> response whose single result carries
    /// <paramref name="contentListJson"/> as the JSON-encoded <c>content_list</c> string.
    /// </summary>
    private static HttpResponseMessage FileParseResponse(string contentListJson, HttpStatusCode status = HttpStatusCode.OK)
    {
        var payload = new
        {
            results = new Dictionary<string, object>
            {
                ["book"] = new { content_list = contentListJson },
            },
        };
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
    }

    private static (MinerUPdfConverter Sut, StubHandler Handler) BuildSut(HttpResponseMessage response)
    {
        var handler = new StubHandler(response);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler));

        var sut = new MinerUPdfConverter(
            factory,
            Options.Create(new MinerUOptions { ServiceUrl = "http://mineru:8000" }),
            NullLogger<MinerUPdfConverter>.Instance);

        return (sut, handler);
    }

    private static async Task<string> WriteTempPdfAsync()
    {
        var pdfPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
        await File.WriteAllBytesAsync(pdfPath, [0x25, 0x50, 0x44, 0x46]); // %PDF
        return pdfPath;
    }

    [Fact]
    public async Task Maps_text_level_to_section_header_text_to_text_and_drops_others()
    {
        const string contentList = """
        [
          {"type":"text","text":"BARBARIAN","text_level":2,"page_idx":0},
          {"type":"text","text":"A fierce warrior.","page_idx":0},
          {"type":"image","text":"","page_idx":0},
          {"type":"table","text":"garbled table","page_idx":1},
          {"type":"text","text":"RAGE","text_level":2,"page_idx":1}
        ]
        """;

        var (sut, handler) = BuildSut(FileParseResponse(contentList));
        var pdfPath = await WriteTempPdfAsync();

        try
        {
            var doc = await sut.ConvertAsync(pdfPath);

            // The converter POSTs to /file_parse.
            handler.Captured.Should().NotBeNull();
            handler.Captured!.Method.Should().Be(HttpMethod.Post);
            handler.Captured.RequestUri!.AbsoluteUri.Should().Be("http://mineru:8000/file_parse");

            // 2 headings + 1 text; image + table dropped.
            doc.Items.Should().HaveCount(3);
            doc.Items[0].Should().BeEquivalentTo(new PdfStructureItem("section_header", "BARBARIAN", 1, 2));
            doc.Items[1].Should().BeEquivalentTo(new PdfStructureItem("text", "A fierce warrior.", 1, null));
            doc.Items[2].Should().BeEquivalentTo(new PdfStructureItem("section_header", "RAGE", 2, 2)); // page_idx 1 -> page 2
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    [Fact]
    public async Task Throws_when_service_returns_non_success_status()
    {
        var (sut, _) = BuildSut(FileParseResponse("[]", HttpStatusCode.InternalServerError));
        var pdfPath = await WriteTempPdfAsync();

        try
        {
            await sut.Invoking(s => s.ConvertAsync(pdfPath))
                .Should().ThrowAsync<HttpRequestException>();
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    [Fact]
    public async Task Recovers_spell_names_not_tagged_as_headings_via_casting_time_anchor()
    {
        // Spell names are NOT headings (text_level null) — the level/school line is followed by "Casting Time:".
        // Both layouts: name merged with level/school, and a real OCR-style level token.
        const string contentList = """
        [
          {"type":"text","text":"SPELL DESCRIPTIONS","text_level":2,"page_idx":0},
          {"type":"text","text":"ACID SPLASH Conjuration cantrip","page_idx":0},
          {"type":"text","text":"Casting Time: 1 action","page_idx":0},
          {"type":"text","text":"You hurl a bubble of acid.","page_idx":0},
          {"type":"text","text":"ALTER SELF 2nd-leveI transmutation","page_idx":1},
          {"type":"text","text":"Casting Time: 1 action","page_idx":1},
          {"type":"text","text":"You assume a different form.","page_idx":1}
        ]
        """;

        var (sut, _) = BuildSut(FileParseResponse(contentList));
        var pdfPath = await WriteTempPdfAsync();

        try
        {
            var doc = await sut.ConvertAsync(pdfPath);
            var headings = doc.Items.Where(i => i.Type == "section_header").Select(i => i.Text).ToList();

            headings.Should().Contain("ACID SPLASH");   // merged name+school, cut at the school word
            headings.Should().Contain("ALTER SELF");     // merged name+level, cut at the first digit (OCR "2nd-leveI")
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }
}
