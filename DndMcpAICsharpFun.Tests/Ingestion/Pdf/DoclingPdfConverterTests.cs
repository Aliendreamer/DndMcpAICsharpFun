using DndMcpAICsharpFun.Features.Ingestion.Pdf;

namespace DndMcpAICsharpFun.Tests.Ingestion.Pdf;

public sealed class DoclingPdfConverterTests
{
    [Fact]
    public void ParseResponse_DoclingTextsShape_ReturnsItems()
    {
        // Real docling-serve v1 response shape: document.json_content.texts[*]
        // with label, text, and prov[0].page_no.
        var json = """
        {
          "document": {
            "md_content": "# Wizard\n\nA scholarly magic-user.",
            "json_content": {
              "texts": [
                { "label": "section_header", "text": "Wizard",                 "prov": [ { "page_no": 112 } ], "level": 1 },
                { "label": "text",           "text": "A scholarly magic-user.","prov": [ { "page_no": 112 } ] }
              ]
            }
          },
          "status": "success"
        }
        """;

        var doc = DoclingPdfConverter.ParseResponse(json);

        Assert.Equal("# Wizard\n\nA scholarly magic-user.", doc.Markdown);
        Assert.Equal(2, doc.Items.Count);
        Assert.Equal("section_header", doc.Items[0].Type);
        Assert.Equal("Wizard", doc.Items[0].Text);
        Assert.Equal(112, doc.Items[0].PageNumber);
        Assert.Equal(1, doc.Items[0].Level);
        Assert.Equal("text", doc.Items[1].Type);
        Assert.Null(doc.Items[1].Level);
    }

    [Fact]
    public void ParseResponse_EmptyTexts_ReturnsEmptyItems()
    {
        var json = """
        { "document": { "md_content": "", "json_content": { "texts": [] } } }
        """;
        var doc = DoclingPdfConverter.ParseResponse(json);
        Assert.Empty(doc.Items);
    }

    [Fact]
    public void ParseResponse_FallbackShape_StillParses()
    {
        // Older / alternative shape with main_text instead of texts.
        var json = """
        {
          "document": {
            "markdown": "x",
            "json_content": {
              "main_text": [
                { "type": "paragraph", "text": "hi", "page_no": 5 }
              ]
            }
          }
        }
        """;
        var doc = DoclingPdfConverter.ParseResponse(json);
        Assert.Single(doc.Items);
        Assert.Equal("paragraph", doc.Items[0].Type);
        Assert.Equal(5, doc.Items[0].PageNumber);
    }

    [Fact]
    public void ParseResponse_WhitespaceText_Skipped()
    {
        var json = """
        {
          "document": {
            "json_content": {
              "texts": [
                { "label": "text", "text": "   ",  "prov": [ { "page_no": 1 } ] },
                { "label": "text", "text": "real", "prov": [ { "page_no": 1 } ] }
              ]
            }
          }
        }
        """;
        var doc = DoclingPdfConverter.ParseResponse(json);
        Assert.Single(doc.Items);
        Assert.Equal("real", doc.Items[0].Text);
    }
}
