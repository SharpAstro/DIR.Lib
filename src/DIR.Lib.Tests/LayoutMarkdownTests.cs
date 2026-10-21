using System;
using System.Collections.Generic;
using System.Linq;
using DIR.Lib;
using DIR.Lib.Layout;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="Builder.Markdown(string, MarkdownStyle)"/>: a document as a tree whose paragraphs wrap at the width they
/// are given, with its parts told apart by colour. Measured by a stub that gives every character 7 units, so where a
/// word lands is arithmetic.
/// </summary>
public class LayoutMarkdownTests
{
    private sealed class PixelCtx : IMeasureContext<float>
    {
        public Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize) => new(text.Length * 7f, 16f);
        public float ToSurface(float designUnits) => designUnits;
    }

    private static readonly MarkdownStyle Style = new();

    // Every text leaf as it was arranged: what it says, its colour, and where.
    private static List<(string Text, RGBAColor32 Color, Rect<float> At)> Texts(Node tree, float width)
        => [.. Engine.Arrange(tree, new Rect<float>(0, 0, width, 1000), new PixelCtx())
            .Where(a => a.Node is Node.Leaf { Content: Content.Text })
            // A word's text ends in its space; the tests name the word.
            .Select(a => (((Content.Text)((Node.Leaf)a.Node).Content).Value.TrimEnd(), ((Content.Text)((Node.Leaf)a.Node).Content).Color, a.Bounds))];

    [Fact]
    public void AParagraphWrapsItsWordsAtTheWidthItIsGiven()
    {
        var texts = Texts(Builder.Markdown("one two three four five six"), 100f);

        texts.Select(t => t.Text).ShouldBe(["one", "two", "three", "four", "five", "six"]);
        // 100 units hold "one two three" (each word with its space: 28 + 28 + 42), so "four" starts the next line.
        texts.Single(t => t.Text == "four").At.Y.ShouldBeGreaterThan(texts.Single(t => t.Text == "one").At.Y);
        texts.Single(t => t.Text == "four").At.X.ShouldBe(0f);
    }

    [Fact]
    public void StrongEmphasisCodeAndLinksAreToldApartByColour()
    {
        var texts = Texts(Builder.Markdown("plain **strong** *soft* `code` [link](https://x.test)"), 1000f);

        texts.Single(t => t.Text == "plain").Color.ShouldBe(Style.Text);
        texts.Single(t => t.Text == "strong").Color.ShouldBe(Style.Strong);
        texts.Single(t => t.Text == "soft").Color.ShouldBe(Style.Emphasis);
        texts.Single(t => t.Text == "code").Color.ShouldBe(Style.Code);
        texts.Single(t => t.Text == "link").Color.ShouldBe(Style.Link);
    }

    [Fact]
    public void PunctuationAfterAStyledWordStaysWithIt()
    {
        var texts = Texts(Builder.Markdown("see **this**, then"), 1000f);

        var word = texts.Single(t => t.Text == "this");
        var comma = texts.Single(t => t.Text == ",");
        // Side by side, no gap: one word in two colours.
        comma.At.X.ShouldBe(word.At.X + word.At.Width);
        comma.At.Y.ShouldBe(word.At.Y);
    }

    [Fact]
    public void HeadingsListsCodeAndTablesEachGetTheirShape()
    {
        var markdown = "# Title\n\n- first\n- second\n\n```\nvar x = 1;\n```\n\n| a | b |\n|---|---|\n| 1 | 2 |\n";
        var texts = Texts(Builder.Markdown(markdown), 400f);

        texts.Single(t => t.Text == "Title").Color.ShouldBe(Style.Strong);
        texts.Count(t => t.Text == "•").ShouldBe(2);
        texts.Single(t => t.Text == "var x = 1;").Color.ShouldBe(Style.Code);
        // The table's cells sit in a row each: a and b on one line, 1 and 2 on the next.
        texts.Single(t => t.Text == "a").At.Y.ShouldBe(texts.Single(t => t.Text == "b").At.Y);
        texts.Single(t => t.Text == "1").At.Y.ShouldBeGreaterThan(texts.Single(t => t.Text == "a").At.Y);
        texts.Single(t => t.Text == "b").At.X.ShouldBeGreaterThan(texts.Single(t => t.Text == "a").At.X);
    }

    [Fact]
    public void ADocumentBesideALabelWrapsAtTheWidthLeftToIt()
    {
        // As a transcript row has it: a fixed label, then the document in what is left.
        var tree = Builder.VStack(Builder.HStack(Builder.Text("claude").WFixed(50f), Builder.Markdown("- one two three four five six").WStar()).WStar());

        var texts = Texts(tree, 150f);

        // 100 units are left to the document and its bullet: the list item's words still wrap, and none sit alone on a line.
        var words = texts.Where(t => t.Text is "one" or "two" or "three" or "four" or "five" or "six").ToList();
        words.Select(w => w.At.Y).Distinct().Count().ShouldBeInRange(2, 3, string.Join(" ", words.Select(w => $"{w.Text}@{w.At.X},{w.At.Y},{w.At.Width}")));
        words.ShouldAllBe(w => w.At.X >= 50f);
    }

    [Fact]
    public void ALinkOpensItsTargetWhenTheStyleSaysHow()
    {
        var opened = new List<string>();
        var tree = Builder.Markdown("[docs](https://x.test/docs)", Style with { OnLink = opened.Add });

        var link = Engine.Arrange(tree, new Rect<float>(0, 0, 400, 100), new PixelCtx())
            .Select(a => a.Node).OfType<Node.Leaf>()
            .Single(l => l.Content is Content.Text { Value: "docs " });

        link.Hit.ShouldBe(new HitResult.LinkHit("https://x.test/docs"));
        link.OnClick.ShouldNotBeNull().Invoke(InputModifier.None);
        opened.ShouldBe(["https://x.test/docs"]);
    }
}
