using System.Collections.Immutable;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>Unit tests for <see cref="MenuModel"/> navigation and key-handling logic.</summary>
public class MenuModelTests
{
    private static MenuModel ThreeItem()
    {
        var m = new MenuModel();
        m.Reset("Title", "Pick one", ["Alpha", "Beta", "Gamma"]);
        return m;
    }

    // --- MoveUp / MoveDown wrapping ---

    [Fact]
    public void MoveDown_WrapsFromLastToFirst()
    {
        var m = ThreeItem();
        m.Reset("T", "P", ["A", "B", "C"], selected: 2);
        m.MoveDown();
        m.SelectedIndex.ShouldBe(0);
    }

    [Fact]
    public void MoveUp_WrapsFromFirstToLast()
    {
        var m = ThreeItem();
        m.MoveUp();
        m.SelectedIndex.ShouldBe(2);
    }

    [Fact]
    public void MoveDown_StepsForward()
    {
        var m = ThreeItem();
        m.MoveDown();
        m.SelectedIndex.ShouldBe(1);
    }

    // --- Enter confirms at current index ---

    [Fact]
    public void HandleKey_Enter_ConfirmsAtCurrentIndex()
    {
        var m = ThreeItem();
        m.MoveDown(); // index 1
        var consumed = m.HandleKey(InputKey.Enter);
        consumed.ShouldBeTrue();
        m.IsConfirmed.ShouldBeTrue();
        m.SelectedIndex.ShouldBe(1);
    }

    // --- D1..D9 select + confirm ---

    [Fact]
    public void HandleKey_D3_SelectsIndexTwoAndConfirms()
    {
        var m = ThreeItem();
        var consumed = m.HandleKey(InputKey.D3);
        consumed.ShouldBeTrue();
        m.IsConfirmed.ShouldBeTrue();
        m.SelectedIndex.ShouldBe(2);
    }

    [Fact]
    public void HandleKey_D9_OutOfRange_NotConsumed()
    {
        var m = ThreeItem(); // only 3 items: D4..D9 are out of range
        var consumed = m.HandleKey(InputKey.D9);
        consumed.ShouldBeFalse();
        m.IsConfirmed.ShouldBeFalse();
    }

    // --- Reset clears IsConfirmed and clamps ---

    [Fact]
    public void Reset_ClearsIsConfirmed()
    {
        var m = ThreeItem();
        m.HandleKey(InputKey.Enter);
        m.IsConfirmed.ShouldBeTrue();
        m.Reset("T2", "P2", ["X", "Y"], selected: 1);
        m.IsConfirmed.ShouldBeFalse();
        m.SelectedIndex.ShouldBe(1);
    }

    [Fact]
    public void Reset_ClampsSelectedAboveLength()
    {
        var m = new MenuModel();
        m.Reset("T", "P", ["A", "B"], selected: 99);
        m.SelectedIndex.ShouldBe(1);
    }

    [Fact]
    public void Reset_ClampsSelectedBelowZero()
    {
        var m = new MenuModel();
        m.Reset("T", "P", ["A", "B"], selected: -5);
        m.SelectedIndex.ShouldBe(0);
    }

    // --- HandleKey returns false when already confirmed ---

    [Fact]
    public void HandleKey_ReturnsFalseWhenAlreadyConfirmed()
    {
        var m = ThreeItem();
        m.HandleKey(InputKey.Enter);
        var consumed = m.HandleKey(InputKey.Down);
        consumed.ShouldBeFalse();
    }

    // --- HandleKey returns false when no items ---

    [Fact]
    public void HandleKey_ReturnsFalseWhenNoItems()
    {
        var m = new MenuModel();
        m.Reset("T", "P", ImmutableArray<string>.Empty);
        var consumed = m.HandleKey(InputKey.Down);
        consumed.ShouldBeFalse();
    }

    // --- Up / Down consumed when not confirmed ---

    [Fact]
    public void HandleKey_Up_ConsumedAndMovesUp()
    {
        var m = ThreeItem();
        m.Reset("T", "P", ["A", "B", "C"], selected: 1);
        var consumed = m.HandleKey(InputKey.Up);
        consumed.ShouldBeTrue();
        m.SelectedIndex.ShouldBe(0);
    }

    [Fact]
    public void HandleKey_Down_ConsumedAndMovesDown()
    {
        var m = ThreeItem();
        var consumed = m.HandleKey(InputKey.Down);
        consumed.ShouldBeTrue();
        m.SelectedIndex.ShouldBe(1);
    }
}
