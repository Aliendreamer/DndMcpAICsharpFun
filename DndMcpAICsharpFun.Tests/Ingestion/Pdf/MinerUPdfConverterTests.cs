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

    [Fact]
    public async Task Cleans_spell_name_from_heading_when_level_school_suffix_is_present()
    {
        // Headings that carry a merged level/school suffix are cleaned to just the spell name.
        // A heading with no level/school token is emitted unchanged.
        const string contentList = """
        [
          {"type":"text","text":"PRESTIDIGITATIONTransmutation cantrip","text_level":2,"page_idx":0},
          {"type":"text","text":"GUIDING BOLT Ist-level evocation","text_level":2,"page_idx":0},
          {"type":"text","text":"PART 3 | SPELLS","text_level":1,"page_idx":1}
        ]
        """;

        var (sut, _) = BuildSut(FileParseResponse(contentList));
        var pdfPath = await WriteTempPdfAsync();

        try
        {
            var doc = await sut.ConvertAsync(pdfPath);
            var headings = doc.Items
                .Where(i => i.Type == "section_header")
                .Select(i => i.Text)
                .ToList();

            headings.Should().Contain("PRESTIDIGITATION");                     // stripped at "Transmutation"
            headings.Should().Contain("GUIDING BOLT");                          // stripped at "Ist" OCR token
            headings.Should().Contain("PART 3 | SPELLS");                       // no level/school — unchanged
            headings.Should().NotContain("PRESTIDIGITATIONTransmutation cantrip");
            headings.Should().NotContain("GUIDING BOLT Ist-level evocation");
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

    [Fact]
    public async Task School_word_in_spell_name_is_not_overcut_on_cantrip_suffix()
    {
        // Regression: spells whose NAME contains a school word (MINOR ILLUSION, PROGRAMMED ILLUSION)
        // must NOT be trimmed at the first occurrence of that school word.
        // Only the trailing "<school> cantrip" or "<Nth-level> <school>" suffix should be stripped.
        const string contentList = """
        [
          {"type":"text","text":"MINOR ILLUSION Illusion cantrip",        "text_level":2,"page_idx":0},
          {"type":"text","text":"PROGRAMMED ILLUSION Illusion cantrip",   "text_level":2,"page_idx":0},
          {"type":"text","text":"PRESTIDIGITATION Transmutation cantrip", "text_level":2,"page_idx":0},
          {"type":"text","text":"SHIELD OF FAITH 1st-level abjuration",  "text_level":2,"page_idx":1},
          {"type":"text","text":"ALTER SELF 2nd-level transmutation",     "text_level":2,"page_idx":1}
        ]
        """;

        var (sut, _) = BuildSut(FileParseResponse(contentList));
        var pdfPath = await WriteTempPdfAsync();

        try
        {
            var doc = await sut.ConvertAsync(pdfPath);
            var headings = doc.Items
                .Where(i => i.Type == "section_header")
                .Select(i => i.Text)
                .ToList();

            // Cantrips whose names contain a school word — must keep the full name
            headings.Should().Contain("MINOR ILLUSION",      "school word in name must not be cut");
            headings.Should().Contain("PROGRAMMED ILLUSION", "school word in name must not be cut");
            headings.Should().Contain("PRESTIDIGITATION",    "no school word in name, cut at suffix school");

            // Leveled spells — cut at first digit (unchanged behaviour)
            headings.Should().Contain("SHIELD OF FAITH", "cut at '1' digit, not at 'abjuration'");
            headings.Should().Contain("ALTER SELF",      "cut at '2' digit, not at 'transmutation'");

            // Raw merged text must not survive
            headings.Should().NotContain("MINOR ILLUSION Illusion cantrip");
            headings.Should().NotContain("PROGRAMMED ILLUSION Illusion cantrip");
            headings.Should().NotContain("MINOR",       "must not over-cut the name to only first word");
            headings.Should().NotContain("PROGRAMMED",  "must not over-cut the name to only first word");
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }

        [Fact]
    public async Task Race_traits_heading_renames_x_traits_to_x_when_bare_name_not_already_emitted()
    {
        // RENAME strategy: "GNOME TRAITS" with no preceding bare "GNOME" heading
        // → the converter emits a section_header "GNOME" in place of "GNOME TRAITS"
        //   so the body that follows belongs to the "GNOME" section.
        //   "GNOME TRAITS" must NOT appear as a separate heading.
        //
        // When a bare race title WAS already emitted ("DWARF"), "DWARF TRAITS" is kept
        // unchanged (it becomes a subsection). "DWARF" must not be duplicated.
        const string contentList = """
        [
          {"type":"text","text":"GNOME TRAITS","text_level":2,"page_idx":0},
          {"type":"text","text":"Gnomes are small, quick folk.",  "page_idx":0},
          {"type":"text","text":"DWARF","text_level":2,"page_idx":1},
          {"type":"text","text":"Bold and hardy.",               "page_idx":1},
          {"type":"text","text":"DWARF TRAITS","text_level":2,"page_idx":1}
        ]
        """;

        var (sut, _) = BuildSut(FileParseResponse(contentList));
        var pdfPath = await WriteTempPdfAsync();

        try
        {
            var doc = await sut.ConvertAsync(pdfPath);
            var headings = doc.Items
                .Where(i => i.Type == "section_header")
                .Select(i => i.Text)
                .ToList();

            // "GNOME TRAITS" is renamed to "GNOME" — the race section now owns the body below it
            headings.Should().Contain("GNOME", "the race name is emitted in place of 'GNOME TRAITS'");
            headings.Should().NotContain("GNOME TRAITS", "rename replaces the TRAITS heading — no separate 'GNOME TRAITS'");

            // "DWARF" already exists → "DWARF TRAITS" is kept unchanged, no duplicate "DWARF"
            headings.Count(h => h == "DWARF").Should().Be(1, "DWARF must not be duplicated by the TRAITS fallback");
            headings.Should().Contain("DWARF TRAITS", "the TRAITS subsection is emitted when bare name was already seen");
        }
        finally
        {
            File.Delete(pdfPath);
        }
    }
}
