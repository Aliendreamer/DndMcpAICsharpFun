using System.Text.Json.Nodes;
using DndMcpAICsharpFun.Features.Ingestion.Programmatic;
using FluentAssertions;

namespace DndMcpAICsharpFun.Tests.Programmatic;

public class TextMatchTests
{
    private static JsonNode? N(string json) => JsonNode.Parse(json);

    [Theory]
    [InlineData("well-known", "well-known")]
    [InlineData("a- \n b", "ab")] // Python normalize_text gives "ab" (verified); the brief's "a b" was wrong
    [InlineData("ob-\r\nject", "ob- ject")] // \r is not in [ \t], so \r\n is not joined (same as Python)
    [InlineData("ob-\nject", "object")]
    [InlineData("  a \t b\n\nc  ", "a b c")]
    public void Normalize_matches_python(string input, string expected)
    {
        TextMatch.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void Matches_strings_after_normalisation_but_case_sensitive()
    {
        TextMatch.Matches(N("\"ob-\\nject  x\""), N("\"object x\"")).Should().BeTrue();
        TextMatch.Matches(N("\"Abc\""), N("\"abc\"")).Should().BeFalse();
    }

    [Fact]
    public void Matches_dicts_by_key_set_and_recursively()
    {
        TextMatch.Matches(N("""{"a":1,"b":"x  y"}"""), N("""{"b":"x y","a":1}""")).Should().BeTrue();
        TextMatch.Matches(N("""{"a":1}"""), N("""{"a":1,"b":2}""")).Should().BeFalse();
        TextMatch.Matches(N("""{"a":1}"""), N("""{"c":1}""")).Should().BeFalse();
        TextMatch.Matches(N("""{"a":{"b":1}}"""), N("""{"a":{"b":2}}""")).Should().BeFalse();
    }

    [Fact]
    public void Matches_lists_elementwise_with_same_length()
    {
        TextMatch.Matches(N("""["a","b"]"""), N("""["a","b"]""")).Should().BeTrue();
        TextMatch.Matches(N("""["a","b"]"""), N("""["a"]""")).Should().BeFalse();
        TextMatch.Matches(N("""["a","b"]"""), N("""["b","a"]""")).Should().BeFalse();
    }

    [Fact]
    public void Matches_scalars_and_mismatched_kinds()
    {
        TextMatch.Matches(null, null).Should().BeTrue();
        TextMatch.Matches(N("null"), N("null")).Should().BeTrue();
        TextMatch.Matches(N("true"), N("true")).Should().BeTrue();
        TextMatch.Matches(N("true"), N("false")).Should().BeFalse();
        TextMatch.Matches(N("true"), N("1")).Should().BeFalse();
        TextMatch.Matches(N("1"), N("1.0")).Should().BeTrue();
        TextMatch.Matches(N("1"), N("2")).Should().BeFalse();
        TextMatch.Matches(N("\"1\""), N("1")).Should().BeFalse();
        TextMatch.Matches(N("[]"), N("{}")).Should().BeFalse();
        TextMatch.Matches(null, N("1")).Should().BeFalse();
    }

    [Fact]
    public void TokenF1_cases()
    {
        TextMatch.TokenF1("a b c", "a b c").Should().Be(1);
        TextMatch.TokenF1("a b", "c d").Should().Be(0);
        TextMatch.TokenF1("", "a").Should().Be(0);
        TextMatch.TokenF1("A b", "a B").Should().Be(1);
        // multiset: common=2, p=2/3, r=2/2 -> f1=0.8
        TextMatch.TokenF1("a a", "a a b").Should().BeApproximately(0.8, 1e-9);
        TextMatch.TokenF1("a", "a a").Should().BeApproximately(2.0 / 3, 1e-9);
    }

    [Fact]
    public void FlattenText_joins_string_leaves_in_document_order()
    {
        TextMatch.FlattenText(N("""{"a":"x","b":[ "y", 3, {"c":"z"} ],"d":null}""")).Should().Be("x y z");
        TextMatch.FlattenText(null).Should().Be("");
        TextMatch.FlattenText(N("\"solo\"")).Should().Be("solo");
    }
}
