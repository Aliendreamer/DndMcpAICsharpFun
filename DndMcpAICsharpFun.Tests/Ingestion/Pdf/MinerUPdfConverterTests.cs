using DndMcpAICsharpFun.Features.Ingestion.Pdf;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DndMcpAICsharpFun.Tests.Ingestion.Pdf;

public class MinerUPdfConverterTests
{
    [Fact]
    public async Task Maps_text_level_to_section_header_text_to_text_and_drops_others()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        const string stem = "book";
        var dir = Path.Combine(root, stem, "txt");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, $"{stem}_content_list.json"), """
        [
          {"type":"text","text":"BARBARIAN","text_level":2,"page_idx":0},
          {"type":"text","text":"A fierce warrior.","page_idx":0},
          {"type":"image","text":"","page_idx":0},
          {"type":"table","text":"garbled table","page_idx":1},
          {"type":"text","text":"RAGE","text_level":2,"page_idx":1}
        ]
        """);

        var sut = new MinerUPdfConverter(
            Options.Create(new MinerUOptions { OutputDirectory = root }),
            NullLogger<MinerUPdfConverter>.Instance);

        try
        {
            var doc = await sut.ConvertAsync($"/books/{stem}.pdf");

            // 2 headings + 1 text; image + table dropped
            doc.Items.Should().HaveCount(3);
            doc.Items[0].Should().BeEquivalentTo(new PdfStructureItem("section_header", "BARBARIAN", 1, 2));
            doc.Items[1].Should().BeEquivalentTo(new PdfStructureItem("text", "A fierce warrior.", 1, null));
            doc.Items[2].Should().BeEquivalentTo(new PdfStructureItem("section_header", "RAGE", 2, 2)); // page_idx 1 -> page 2
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Throws_when_content_list_missing()
    {
        var sut = new MinerUPdfConverter(
            Options.Create(new MinerUOptions
            {
                OutputDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            }),
            NullLogger<MinerUPdfConverter>.Instance);

        await sut.Invoking(s => s.ConvertAsync("/books/missing.pdf"))
            .Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task Recovers_spell_names_not_tagged_as_headings_via_casting_time_anchor()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        const string stem = "spells";
        var dir = Path.Combine(root, stem, "txt");
        Directory.CreateDirectory(dir);
        // Spell names are NOT headings (text_level null) — the level/school line is followed by "Casting Time:".
        // Both layouts: name merged with level/school, and a real OCR-style level token.
        await File.WriteAllTextAsync(Path.Combine(dir, $"{stem}_content_list.json"), """
        [
          {"type":"text","text":"SPELL DESCRIPTIONS","text_level":2,"page_idx":0},
          {"type":"text","text":"ACID SPLASH Conjuration cantrip","page_idx":0},
          {"type":"text","text":"Casting Time: 1 action","page_idx":0},
          {"type":"text","text":"You hurl a bubble of acid.","page_idx":0},
          {"type":"text","text":"ALTER SELF 2nd-leveI transmutation","page_idx":1},
          {"type":"text","text":"Casting Time: 1 action","page_idx":1},
          {"type":"text","text":"You assume a different form.","page_idx":1}
        ]
        """);

        var sut = new MinerUPdfConverter(
            Options.Create(new MinerUOptions { OutputDirectory = root }),
            NullLogger<MinerUPdfConverter>.Instance);

        try
        {
            var doc = await sut.ConvertAsync($"/books/{stem}.pdf");
            var headings = doc.Items.Where(i => i.Type == "section_header").Select(i => i.Text).ToList();

            headings.Should().Contain("ACID SPLASH");   // merged name+school, cut at the school word
            headings.Should().Contain("ALTER SELF");     // merged name+level, cut at the first digit (OCR "2nd-leveI")
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
