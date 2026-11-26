using System.Collections.Immutable;
using System.Numerics;
using DIR.Lib;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="KeptLayout{TKey}"/> hands back the tree it kept only while every input the build read is unchanged:
/// the widget's own key, and the bounds, scale, font and theme generation it asks for by name.
/// </summary>
public class KeptLayoutTests
{
    private readonly record struct Key(int Count, bool Active);

    private static readonly RectF32 Bounds = new(new Vector2(10, 20), new Vector2(300, 40));
    private static readonly ImmutableArray<Layout.ArrangedNode<float>> Tree = ImmutableArray<Layout.ArrangedNode<float>>.Empty;

    private static KeptLayout<Key> Kept()
    {
        var kept = new KeptLayout<Key>();
        kept.Keep(new Key(3, true), Bounds, DesignScale.One, "font.ttf", 7, Tree);
        return kept;
    }

    [Fact]
    public void NothingIsKeptBeforeTheFirstBuild()
    {
        var kept = new KeptLayout<Key>();
        kept.TryGet(new Key(3, true), Bounds, DesignScale.One, "font.ttf", 7, out _).ShouldBeFalse();
        kept.Builds.ShouldBe(0);
    }

    [Fact]
    public void TheSameInputsGetTheKeptTree()
    {
        var kept = Kept();
        kept.TryGet(new Key(3, true), Bounds, DesignScale.One, "font.ttf", 7, out var arranged).ShouldBeTrue();
        arranged.ShouldBe(Tree);
        kept.Builds.ShouldBe(1);
    }

    [Fact]
    public void EachInputTheBuildReadDropsTheTreeWhenItChanges()
    {
        var kept = Kept();
        kept.TryGet(new Key(4, true), Bounds, DesignScale.One, "font.ttf", 7, out _).ShouldBeFalse("the widget's key");
        kept.TryGet(new Key(3, true), new RectF32(new Vector2(10, 20), new Vector2(301, 40)), DesignScale.One, "font.ttf", 7, out _)
            .ShouldBeFalse("the bounds");
        kept.TryGet(new Key(3, true), Bounds, new DesignScale(2, 2), "font.ttf", 7, out _).ShouldBeFalse("the scale");
        kept.TryGet(new Key(3, true), Bounds, DesignScale.One, "other.ttf", 7, out _).ShouldBeFalse("the font");
        kept.TryGet(new Key(3, true), Bounds, DesignScale.One, null, 7, out _).ShouldBeFalse("no font");
        kept.TryGet(new Key(3, true), Bounds, DesignScale.One, "font.ttf", 8, out _).ShouldBeFalse("the theme generation");
    }

    [Fact]
    public void KeepingAgainCountsABuildAndTakesTheNewInputs()
    {
        var kept = Kept();
        kept.Keep(new Key(5, false), Bounds, DesignScale.One, "font.ttf", 7, Tree);
        kept.Builds.ShouldBe(2);
        kept.TryGet(new Key(5, false), Bounds, DesignScale.One, "font.ttf", 7, out _).ShouldBeTrue();
        kept.TryGet(new Key(3, true), Bounds, DesignScale.One, "font.ttf", 7, out _).ShouldBeFalse();
    }

    [Fact]
    public void ForgetMakesTheNextFrameBuild()
    {
        var kept = Kept();
        kept.Forget();
        kept.TryGet(new Key(3, true), Bounds, DesignScale.One, "font.ttf", 7, out _).ShouldBeFalse();
    }
}
