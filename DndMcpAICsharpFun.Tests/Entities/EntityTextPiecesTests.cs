using DndMcpAICsharpFun.Features.Entities.CanonicalText;

using FluentAssertions;

using Xunit;

namespace DndMcpAICsharpFun.Tests.Entities;

public sealed class EntityTextPiecesTests
{
    [Fact]
    public void Short_text_is_one_piece()
    {
        var piece = EntityTextPieces.Split("Fireball is bright.").Should().ContainSingle().Subject;
        piece.Should().Be(new TextPiece(0, "Fireball is bright."));
    }

    [Fact]
    public void Every_character_of_a_long_text_is_in_some_piece_and_no_piece_is_too_long()
    {
        var text = string.Concat(Enumerable.Range(0, 900).Select(i => $"Word{i} says something. "));
        var pieces = EntityTextPieces.Split(text);
        pieces.Should().OnlyContain(p => p.Text.Length <= EntityTextPieces.MaxChars && text.Substring(p.Start, p.Text.Length) == p.Text);
        var covered = new bool[text.Length];
        foreach (var p in pieces) Array.Fill(covered, true, p.Start, p.Text.Length);
        covered.Should().NotContain(false);
        pieces.Zip(pieces.Skip(1)).Should().OnlyContain(x => x.Second.Start < x.First.Start + x.First.Text.Length, "pieces overlap");
    }

    [Fact]
    public void Text_without_any_break_is_still_split()
    {
        var pieces = EntityTextPieces.Split(new string('x', 5000));
        pieces.Should().HaveCountGreaterThan(3);
        pieces[^1].Start.Should().BeLessThan(5000);
        (pieces[^1].Start + pieces[^1].Text.Length).Should().Be(5000);
    }

    [Fact]
    public void Embedding_texts_head_later_pieces_and_add_notes_and_lore()
    {
        var text = string.Concat(Enumerable.Repeat("The demon roars loudly. ", 100));
        var texts = EntityTextPieces.ForEmbedding("Demogorgon", "Monster", text, notes: null, extra: ["Demogorgon\nA prince with two heads."]);
        texts[0].Should().StartWith("The demon roars");
        texts[1].Should().StartWith("Demogorgon (Monster): ");
        texts[^1].Should().Be("Demogorgon (Monster): Demogorgon\nA prince with two heads.");
    }

    [Fact]
    public void Window_returns_short_text_whole()
    {
        EntityTextPieces.Window("short", new TextPiece(0, "short"), 3000).Should().Be("short");
    }

    [Fact]
    public void Window_contains_the_best_piece_and_respects_the_limit()
    {
        var text = string.Concat(Enumerable.Range(0, 1000).Select(i => $"Line {i} here. "));
        var best = EntityTextPieces.Split(text)[5];
        var window = EntityTextPieces.Window(text, best, 3000);
        window.Should().Contain(best.Text.Trim());
        window.Length.Should().BeLessThanOrEqualTo(3002);
        window.Should().StartWith("…").And.EndWith("…");
    }
}