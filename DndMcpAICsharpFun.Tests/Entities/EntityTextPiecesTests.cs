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


    private static readonly string Filler = string.Concat(Enumerable.Repeat("Barbarians rage in battle and rest. ", 60));

    [Fact]
    public void The_best_matching_piece_and_the_opening_are_shown_in_book_order()
    {
        var text = Filler + "Level Rages Rage Damage 17th +6 Brutal Critical 6 +4. " + Filler + Filler;
        var pieces = EntityTextPieces.Split(text);
        var best = pieces.Select((p, i) => (p, i)).First(x => x.p.Text.Contains("17th")).i;
        var vectors = pieces.Select((_, i) => i == best ? new[] { 1f, 0f } : new[] { 0f, 1f }).ToList();

        var shown = EntityTextPieces.Matching(text, null, vectors, [1f, 0f], 3000, pieces[^1]);

        shown.Should().StartWith(pieces[0].Text[..100]).And.Contain("17th +6 Brutal Critical 6 +4");
        shown.Length.Should().BeLessThanOrEqualTo(3010);
    }

    [Fact]
    public void A_narrow_window_shows_only_the_best_piece()
    {
        var text = Filler + "Quickened Spell. Spend 2 sorcery points. " + Filler;
        var pieces = EntityTextPieces.Split(text);
        var vectors = pieces.Select(p => p.Text.Contains("Quickened") ? new[] { 1f, 0f } : new[] { 0f, 1f }).ToList();

        var shown = EntityTextPieces.Matching(text, null, vectors, [1f, 0f], 1500, pieces[0]);

        shown.Should().Contain("Quickened Spell").And.NotStartWith(pieces[0].Text[..100]);
    }

    [Fact]
    public void Without_piece_vectors_the_window_falls_back_to_the_given_piece()
    {
        var text = Filler + Filler;
        var pieces = EntityTextPieces.Split(text);

        EntityTextPieces.Matching(text, null, null, [1f, 0f], 1500, pieces[1])
            .Should().Be(EntityTextPieces.Window(text, pieces[1], 1500));
    }

    [Fact]
    public void Note_pieces_past_the_text_are_never_shown()
    {
        var text = Filler + Filler;
        var notes = new[] { new DndMcpAICsharpFun.Domain.Entities.EntityNote(null, string.Concat(Enumerable.Repeat("margin ", 400)), 1) };
        var embedded = EntityTextPieces.Split(EntityEmbeddingText.For(text, notes));
        var vectors = embedded.Select((_, i) => i == embedded.Count - 1 ? new[] { 1f, 0f } : new[] { 0f, 1f }).ToList();

        EntityTextPieces.Matching(text, notes, vectors, [1f, 0f], 3000, embedded[0]).Should().NotContain("margin");
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

    [Fact]
    public void Outline_names_each_part_by_its_headings_and_Part_returns_it()
    {
        var body = string.Concat(Enumerable.Repeat("Some words of the book. ", 80));
        var text = "SORCERER\n" + body + "\nMETAMAGIC\n" + body + "\nQuickened SPell\n" + body + "\nTwInned SPEll\n" + body;
        var outline = EntityTextPieces.Outline(text);
        var pieces = EntityTextPieces.Split(text);

        outline.Should().HaveCount(pieces.Count);
        outline[0].Should().StartWith("1: Sorcerer");
        var twin = outline.Single(l => l.Contains("Twinned Spell"));
        var n = int.Parse(twin.Split(':')[0]);
        EntityTextPieces.Part(text, n).Should().Contain("TwInned SPEll");
        EntityTextPieces.Part(text, 0).Should().BeNull();
        EntityTextPieces.Part(text, pieces.Count + 1).Should().BeNull();
    }

    [Fact]
    public void Outline_uses_the_opening_words_when_a_part_has_no_heading()
    {
        EntityTextPieces.Outline("no heading here at all, just words.").Should().Equal("1: no heading here at all, just …");
    }
}
