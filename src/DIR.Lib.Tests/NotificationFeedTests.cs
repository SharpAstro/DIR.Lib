using System;
using System.Linq;
using DIR.Lib.Notifications;
using Shouldly;
using Xunit;

namespace DIR.Lib.Tests;

public class NotificationFeedTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static DateTimeOffset At(double s) => T0.AddSeconds(s);

    private static Notification Fade(string t = "f", NotificationSeverity s = NotificationSeverity.Info)
        => new() { Title = t, Severity = s, Kind = NotificationKind.Fading };

    private static Notification Stay(string t = "s", NotificationSeverity s = NotificationSeverity.Warn, Action? a = null)
        => new() { Title = t, Severity = s, Kind = NotificationKind.Staying, Action = a, ActionLabel = a is null ? null : "Go" };

    [Fact]
    public void FadingToastLeavesAfterDefaultFiveSecondsAndIsInHistory()
    {
        var feed = new NotificationFeed();
        var n = feed.Post(Fade(), At(0));
        feed.Advance(At(4.9));
        feed.Toasts.Count.ShouldBe(1);
        feed.History.ShouldBeEmpty();
        feed.Advance(At(5));
        feed.Toasts.ShouldBeEmpty();
        feed.History.Count.ShouldBe(1);
        feed.History[0].Id.ShouldBe(n.Id);
    }

    [Fact]
    public void FadeAfterIsOneSettableValue()
    {
        var feed = new NotificationFeed { FadeAfter = TimeSpan.FromSeconds(2) };
        feed.Post(Fade(), At(0));
        feed.Advance(At(2));
        feed.Toasts.ShouldBeEmpty();
    }

    [Fact]
    public void RemainingFractionCountsDown()
    {
        var feed = new NotificationFeed();
        feed.Post(Fade(), At(0));
        feed.Toasts[0].RemainingFraction.ShouldBe(1.0);
        feed.Advance(At(2.5));
        feed.Toasts[0].RemainingFraction.ShouldBe(0.5, 1e-9);
    }

    [Fact]
    public void PointerOverStopsTheClock()
    {
        var feed = new NotificationFeed();
        var n = feed.Post(Fade(), At(0));
        feed.Advance(At(3));
        feed.PointerOver(n.Id);
        feed.Advance(At(100));
        feed.Toasts.Count.ShouldBe(1);
        feed.PointerOver(null);
        feed.Advance(At(101));
        feed.Toasts.Count.ShouldBe(1);
        feed.Advance(At(102));
        feed.Toasts.ShouldBeEmpty();
        feed.History.Count.ShouldBe(1);
    }

    [Fact]
    public void StayingNeverLeavesByItselfButByDismiss()
    {
        var feed = new NotificationFeed();
        var n = feed.Post(Stay(), At(0));
        feed.Advance(At(100000));
        feed.Toasts.Count.ShouldBe(1);
        feed.NeedsYou.Count.ShouldBe(1);
        feed.Dismiss(n.Id);
        feed.Toasts.ShouldBeEmpty();
        feed.NeedsYou.ShouldBeEmpty();
        feed.History.Count.ShouldBe(1);
        feed.IsHandled(n.Id).ShouldBeFalse();
    }

    [Fact]
    public void StayingLeavesOnItsActionWhichRuns()
    {
        var feed = new NotificationFeed();
        var ran = 0;
        var n = feed.Post(Stay(a: () => ran++), At(0));
        feed.Handle(n.Id);
        ran.ShouldBe(1);
        feed.NeedsYou.ShouldBeEmpty();
        feed.IsHandled(n.Id).ShouldBeTrue();
        feed.Handle(n.Id);
        ran.ShouldBe(1);
    }

    [Fact]
    public void AtMostThreeToastsNewestFirst()
    {
        var feed = new NotificationFeed();
        for (var i = 1; i <= 5; i++)
        {
            feed.Post(Stay(i.ToString()), At(i));
        }
        feed.Toasts.Select(t => t.Notification.Title).ToArray().ShouldBe(["5", "4", "3"]);
    }

    [Fact]
    public void BellCountAndWorstSeverity()
    {
        var feed = new NotificationFeed();
        feed.BellCount.ShouldBe(0);
        feed.WorstSeverity.ShouldBeNull();
        feed.Post(Stay(s: NotificationSeverity.Warn), At(0));
        var e = feed.Post(Stay(s: NotificationSeverity.Error), At(0));
        feed.Post(Fade(s: NotificationSeverity.Error), At(0));
        feed.BellCount.ShouldBe(2);
        feed.WorstSeverity.ShouldBe(NotificationSeverity.Error);
        feed.Dismiss(e.Id);
        feed.WorstSeverity.ShouldBe(NotificationSeverity.Warn);
    }

    [Fact]
    public void HistoryIsPrunedByKeepForAndClearKeepsOpenStaying()
    {
        var feed = new NotificationFeed { KeepFor = TimeSpan.FromMinutes(1) };
        feed.Post(Fade("a"), At(0));
        feed.Post(Stay("open"), At(0));
        feed.Advance(At(5));
        feed.History.Count.ShouldBe(1);
        feed.Advance(At(5 + 59));
        feed.History.Count.ShouldBe(1);
        feed.Advance(At(5 + 61));
        feed.History.ShouldBeEmpty();

        feed.Post(Fade("b"), At(100));
        feed.Advance(At(106));
        feed.History.Count.ShouldBe(1);
        feed.Clear();
        feed.History.ShouldBeEmpty();
        feed.NeedsYou.Count.ShouldBe(1);
    }

    [Fact]
    public void VersionRisesOnChange()
    {
        var feed = new NotificationFeed();
        var seen = 0;
        feed.Changed += () => seen++;
        var v = feed.Version;
        feed.Post(Fade(), At(0));
        feed.Version.ShouldBeGreaterThan(v);
        seen.ShouldBeGreaterThan(0);
    }
}
