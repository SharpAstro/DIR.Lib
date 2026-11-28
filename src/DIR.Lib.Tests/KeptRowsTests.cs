using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="KeptRows{TId, TKey}"/> keeps each row's node while its key stands, so the measure memo and a scrolled
/// stack's kept extents (both keyed by node reference) hit for a host that builds its tree every frame (DIR.Lib #338).
/// </summary>
public class KeptRowsTests(ITestOutputHelper output)
{
    private const float Width = 600f;
    private const float ViewportH = 400f;
    private const float Pad = 6f;
    private const float RowGap = 4f;
    private const float Font = 10f;

    private readonly record struct RowKey(int Index, int Extra, int Theme);

    private static Layout.Node BuildRow(RowKey k)
    {
        var words = new Layout.Node[4 + (k.Index * 7) % 61 + k.Extra];
        for (var w = 0; w < words.Length; w++)
        {
            words[w] = Layout.Builder.Text(new string((char)('a' + (k.Index + w) % 26), 3 + (k.Index + w) % 5), Font);
        }

        return Layout.Builder.HStack(
                Layout.Builder.Text("user", Font),
                Layout.Builder.WrapH(words).WithGap(4f).WithLineGap(2f).WStar().HAuto())
            .WithGap(8f).Pad(Pad).WStar().HAuto()
            .Clickable(new HitResult.ListItemHit("chat", k.Index), _ => { });
    }

    [Fact]
    public void TheSameIdAndAnEqualKeyReturnTheSameNodeWithNoBuild()
    {
        var kept = new KeptRows<int, RowKey>();
        var key = new RowKey(1, 0, 0);
        var first = kept.Row(1, key, BuildRow);
        kept.EndFrame();
        var second = kept.Row(1, new RowKey(1, 0, 0), BuildRow);

        second.ShouldBeSameAs(first);
        kept.Builds.ShouldBe(1);
    }

    [Fact]
    public void AChangedKeyRebuildsThatRowAlone()
    {
        var kept = new KeptRows<int, RowKey>();
        var a = kept.Row(1, new RowKey(1, 0, 0), BuildRow);
        var b = kept.Row(2, new RowKey(2, 0, 0), BuildRow);
        kept.EndFrame();

        var a2 = kept.Row(1, new RowKey(1, 0, 1), BuildRow);
        var b2 = kept.Row(2, new RowKey(2, 0, 0), BuildRow);

        a2.ShouldNotBeSameAs(a);
        b2.ShouldBeSameAs(b);
        kept.Builds.ShouldBe(3);
    }

    [Fact]
    public void ARowNotAskedForInAFrameIsDroppedAtItsEnd()
    {
        var kept = new KeptRows<int, RowKey>();
        var gone = kept.Row(1, new RowKey(1, 0, 0), BuildRow);
        kept.Row(2, new RowKey(2, 0, 0), BuildRow);
        kept.EndFrame();
        kept.Count.ShouldBe(2);

        kept.Row(2, new RowKey(2, 0, 0), BuildRow);
        kept.EndFrame();
        kept.Count.ShouldBe(1, "row 1 was not asked for");

        kept.Row(1, new RowKey(1, 0, 0), BuildRow).ShouldNotBeSameAs(gone);
        kept.Builds.ShouldBe(3);
    }

    [Fact]
    public void ForgetDropsEverythingSoTheNextFrameBuilds()
    {
        var kept = new KeptRows<int, RowKey>();
        var first = kept.Row(1, new RowKey(1, 0, 0), BuildRow);
        kept.Forget();
        kept.Count.ShouldBe(0);
        kept.Row(1, new RowKey(1, 0, 0), BuildRow).ShouldNotBeSameAs(first);
    }

    private sealed class CountingContext : Layout.IMeasureContext<float>
    {
        public int TextCalls { get; set; }

        public object? MeasureKey => "kept-rows";

        public Layout.Size<float> MeasureText(ReadOnlySpan<char> text, float fontSize)
        {
            TextCalls++;
            return new(text.Length * fontSize, fontSize);
        }

        public float ToSurface(float designUnits) => designUnits;
    }

    private static Layout.Node Frame(KeptRows<int, RowKey> kept, int count, int changed, ListScrollController scroll)
    {
        var rows = new Layout.Node[count];
        for (var i = 0; i < count; i++)
        {
            rows[i] = kept.Row(i, new RowKey(i, i == changed ? 40 : 0, 0), BuildRow);
        }

        kept.EndFrame();
        return Layout.Builder.VStack(rows).WithGap(RowGap).WStar().HClamp(0f, ViewportH).Pad(Pad).WithScroll(scroll);
    }

    [Fact]
    public void ALongListBuiltThroughKeptRowsEveryFrameMeasuresTheWindowAndTheChangedRowOnly()
    {
        const int Count = 3000;
        var kept = new KeptRows<int, RowKey>();
        var scroll = new ListScrollController();
        var ctx = new CountingContext();
        var bounds = new Rect<float>(0f, 0f, Width, ViewportH);

        Layout.Engine.Arrange(Frame(kept, Count, -1, scroll), bounds, ctx);
        scroll.AtomOffset = 1000;
        Layout.Engine.Arrange(Frame(kept, Count, -1, scroll), bounds, ctx);
        kept.Builds.ShouldBe(Count);

        var lookups = Layout.MeasureMemo<float>.Lookups;
        ctx.TextCalls = 0;
        Layout.Engine.Arrange(Frame(kept, Count, -1, scroll), bounds, ctx);
        var baseline = Layout.MeasureMemo<float>.Lookups - lookups;
        var baselineText = ctx.TextCalls;

        lookups = Layout.MeasureMemo<float>.Lookups;
        ctx.TextCalls = 0;
        var buildsBefore = kept.Builds;
        Layout.Engine.Arrange(Frame(kept, Count, 2500, scroll), bounds, ctx);
        var after = Layout.MeasureMemo<float>.Lookups - lookups;
        var changedLeaves = 2 + 4 + (2500 * 7) % 61 + 40;

        output.WriteLine($"kept frame: {baseline} lookups, {baselineText} MeasureText; one row changed: {after} lookups, {ctx.TextCalls} MeasureText");
        (kept.Builds - buildsBefore).ShouldBe(1);
        baselineText.ShouldBe(0, "an unchanged frame measures no text");
        (after - baseline).ShouldBeGreaterThan(0);
        (after - baseline).ShouldBeLessThanOrEqualTo(4 * changedLeaves, "only the changed row adds lookups");
        ctx.TextCalls.ShouldBeLessThanOrEqualTo(2 * changedLeaves, "and its leaves bound the text measures");
    }
}
