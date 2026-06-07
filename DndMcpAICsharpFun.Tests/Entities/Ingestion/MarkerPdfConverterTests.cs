using System.Text.Json;
using DndMcpAICsharpFun.Features.Ingestion.Pdf;

namespace DndMcpAICsharpFun.Tests.Entities.Ingestion;

/// <summary>
/// Unit tests for the STATIC mapping layer <see cref="MarkerPdfConverter.FromMarkerJson"/>.
/// These are pure in-memory tests — no HTTP, no DI required.
/// </summary>
public sealed class MarkerPdfConverterTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static JsonElement ParseJson(string json) =>
        JsonDocument.Parse(json).RootElement;

    private static JsonElement SinglePageDoc(string blockType, string html, string pageId = "/page/4") =>
        ParseJson($$"""
        {
          "children": [
            {
              "id": "{{pageId}}",
              "block_type": "Page",
              "children": [
                {
                  "block_type": "{{blockType}}",
                  "html": "{{html}}"
                }
              ]
            }
          ]
        }
        """);

    // ── heading mapping ───────────────────────────────────────────────────────

    [Fact]
    public void SectionHeader_MapsToHeading_WithLevelAndPage()
    {
        // page id /page/4 → page 5 (0-based + 1)
        var doc = SinglePageDoc("SectionHeader", "<h2>FIREBALL</h2>");

        var result = MarkerPdfConverter.FromMarkerJson(doc);

        var item = Assert.Single(result.Items);
        Assert.Equal("section_header", item.Type);
        Assert.Equal("FIREBALL", item.Text);
        Assert.Equal(2, item.Level);
        Assert.Equal(5, item.PageNumber);
    }

    [Fact]
    public void SectionHeader_H3_MapsLevel3()
    {
        var doc = SinglePageDoc("SectionHeader", "<h3>SPELLS</h3>", "/page/0");

        var result = MarkerPdfConverter.FromMarkerJson(doc);

        var item = Assert.Single(result.Items);
        Assert.Equal(3, item.Level);
        Assert.Equal(1, item.PageNumber); // page/0 → page 1
    }

    // ── dice-caption demotion ─────────────────────────────────────────────────

    [Fact]
    public void SectionHeader_LowercaseDicePrefix_DemotedToText()
    {
        // "d4 Desired Offering" — starts with d<digit>; must become a text item, not heading
        var doc = SinglePageDoc("SectionHeader", "<h2>d4 Desired Offering</h2>");

        var result = MarkerPdfConverter.FromMarkerJson(doc);

        var item = Assert.Single(result.Items);
        Assert.Equal("text", item.Type);
        Assert.Null(item.Level);
    }

    [Fact]
    public void SectionHeader_UppercaseDicePrefix_DemotedToText()
    {
        // "D8 Magical Effect" — starts with D<digit>
        var doc = SinglePageDoc("SectionHeader", "<h3>D8 Magical Effect</h3>");

        var result = MarkerPdfConverter.FromMarkerJson(doc);

        var item = Assert.Single(result.Items);
        Assert.Equal("text", item.Type);
        Assert.Null(item.Level);
    }

    [Fact]
    public void SectionHeader_DicePrefix_WithExtraSpaces_DemotedToText()
    {
        // stripped text "d20 Wild Magic Surge Table" — d<digit> at start
        var doc = SinglePageDoc("SectionHeader", "<h1>d20 Wild Magic Surge Table</h1>");

        var result = MarkerPdfConverter.FromMarkerJson(doc);

        var item = Assert.Single(result.Items);
        Assert.Equal("text", item.Type);
    }

    // ── despacer applied to headings ──────────────────────────────────────────

    [Fact]
    public void SectionHeader_GarbledAllCaps_IsNormalizedByDespacer()
    {
        // "ABER R ATIONS" → "ABERRATIONS"
        var doc = SinglePageDoc("SectionHeader", "<h2>ABER R ATIONS</h2>");

        var result = MarkerPdfConverter.FromMarkerJson(doc);

        var item = Assert.Single(result.Items);
        Assert.Equal("section_header", item.Type);
        Assert.Equal("ABERRATIONS", item.Text);
    }

    [Fact]
    public void TextBlock_IsNotDespaced()
    {
        // Despacer must NOT be applied to non-heading text items
        const string rawText = "ABER R ATIONS";
        var doc = ParseJson($$"""
        {
          "children": [
            {
              "id": "/page/0",
              "block_type": "Page",
              "children": [
                {
                  "block_type": "Text",
                  "html": "<p>{{rawText}}</p>"
                }
              ]
            }
          ]
        }
        """);

        var result = MarkerPdfConverter.FromMarkerJson(doc);

        var item = Assert.Single(result.Items);
        Assert.Equal("text", item.Type);
        // body after HTML stripping is the raw text — despacer NOT applied
        Assert.Equal(rawText, item.Text);
    }

    // ── skip noisy block types ────────────────────────────────────────────────

    [Theory]
    [InlineData("PageHeader")]
    [InlineData("PageFooter")]
    [InlineData("Picture")]
    [InlineData("Figure")]
    public void NoisyBlockTypes_AreSkipped(string blockType)
    {
        var doc = SinglePageDoc(blockType, "<p>some noise</p>");

        var result = MarkerPdfConverter.FromMarkerJson(doc);

        Assert.Empty(result.Items);
    }

    // ── Group / ListGroup recursion ───────────────────────────────────────────

    [Fact]
    public void Group_Recursion_ReachesInnerTextBlocks()
    {
        var doc = ParseJson("""
        {
          "children": [
            {
              "id": "/page/2",
              "block_type": "Page",
              "children": [
                {
                  "block_type": "Group",
                  "html": "",
                  "children": [
                    {
                      "block_type": "ListGroup",
                      "html": "",
                      "children": [
                        {
                          "block_type": "ListItem",
                          "html": "<p>Item one</p>"
                        },
                        {
                          "block_type": "ListItem",
                          "html": "<p>Item two</p>"
                        }
                      ]
                    }
                  ]
                }
              ]
            }
          ]
        }
        """);

        var result = MarkerPdfConverter.FromMarkerJson(doc);

        Assert.Equal(2, result.Items.Count);
        Assert.All(result.Items, i => Assert.Equal("text", i.Type));
        Assert.Equal("Item one", result.Items[0].Text);
        Assert.Equal("Item two", result.Items[1].Text);
    }

    [Fact]
    public void TextLeaf_EmptyHtml_WithChildren_Recurses()
    {
        // A "Text" block with empty html but children should recurse into children.
        var doc = ParseJson("""
        {
          "children": [
            {
              "id": "/page/0",
              "block_type": "Page",
              "children": [
                {
                  "block_type": "Text",
                  "html": "",
                  "children": [
                    {
                      "block_type": "Text",
                      "html": "<p>Nested content</p>"
                    }
                  ]
                }
              ]
            }
          ]
        }
        """);

        var result = MarkerPdfConverter.FromMarkerJson(doc);

        var item = Assert.Single(result.Items);
        Assert.Equal("Nested content", item.Text);
    }

    // ── page number arithmetic ────────────────────────────────────────────────

    [Fact]
    public void PageId_IsZeroBased_ResultIsOneBased()
    {
        var doc = SinglePageDoc("Text", "<p>hello</p>", "/page/0");
        var result = MarkerPdfConverter.FromMarkerJson(doc);
        Assert.Equal(1, Assert.Single(result.Items).PageNumber);
    }

    [Fact]
    public void PageId_10_MapsTo_11()
    {
        var doc = SinglePageDoc("Text", "<p>hello</p>", "/page/10");
        var result = MarkerPdfConverter.FromMarkerJson(doc);
        Assert.Equal(11, Assert.Single(result.Items).PageNumber);
    }
}
