using System;
using System.Reflection;
using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// The 9.1 lesson, pinned: <b>an optional parameter added to a record's primary constructor DELETES the
/// old constructor from the assembly.</b>
///
/// <para>
/// 9.1 added one to <c>HitResult.TextInputHit</c> and called the release "additive throughout", which is
/// true of the SOURCE and false of the metadata. The published Console.Lib, compiled against 9.0, calls
/// the two-argument form, so against 9.1 every terminal hit test threw <c>MissingMethodException</c> --
/// and it was invisible on a dev box, where the sibling compiles from source and the package path is
/// never taken.
/// </para>
///
/// <para>
/// 9.2 does the same thing to <c>MouseUp</c> and <c>MouseMove</c>, which Console.Lib and
/// SdlVulkan.Renderer both construct. These are the explicit old-arity constructors that make it safe,
/// and the reflection here is the only thing that can see them: a three-argument CALL binds happily to a
/// four-parameter constructor with a default, so nothing written in C# can tell the two apart.
/// </para>
/// </summary>
public class InputEventCompatibilityTests
{
    [Fact]
    public void TextInputHitGetsBackTheOneParameterConstructor91Deleted()
    {
        // The incident itself: Console.Lib 4.33 calls new HitResult.TextInputHit(field.State) in
        // CellLayout.HitOf, and 9.1 removed exactly that. Restoring it is what lets the published
        // Console.Lib run against 9.2 with no rebuild.
        typeof(HitResult.TextInputHit)
            .GetConstructor([typeof(TextInputState)])
            .ShouldNotBeNull("the published Console.Lib calls exactly this");
    }

    [Fact]
    public void MouseUpKeepsItsThreeParameterConstructorInTheASSEMBLY()
    {
        // Console.Lib 4.33: new InputEvent.MouseUp(mouse.X, mouse.Y, button).
        typeof(InputEvent.MouseUp)
            .GetConstructor([typeof(float), typeof(float), typeof(MouseButton)])
            .ShouldNotBeNull("a published host compiled against 9.1 calls exactly this");
    }

    [Fact]
    public void MouseMoveKeepsItsTwoParameterConstructorInTheASSEMBLY()
    {
        // SdlVulkan.Renderer: new InputEvent.MouseMove(x, y).
        typeof(InputEvent.MouseMove)
            .GetConstructor([typeof(float), typeof(float)])
            .ShouldNotBeNull("a published host compiled against 9.1 calls exactly this");
    }

    [Fact]
    public void TheOldArityConstructorsBuildTheSameValueTheNewOnesDefaultTo()
    {
        // Typed against the old signatures, so a change to the parameter list stops this compiling
        // rather than quietly rebinding.
        Func<float, float, MouseButton, InputEvent.MouseUp> up = (x, y, b) => new InputEvent.MouseUp(x, y, b);
        Func<float, float, InputEvent.MouseMove> move = (x, y) => new InputEvent.MouseMove(x, y);

        up(1f, 2f, MouseButton.Left).ShouldBe(new InputEvent.MouseUp(1f, 2f, MouseButton.Left, InputModifier.None));
        move(1f, 2f).ShouldBe(new InputEvent.MouseMove(1f, 2f, MouseButton.None));
    }

    [Fact]
    public void ThePositionalPatternsWrittenAgainstTheOldShapesStillMatch()
    {
        // A record's synthesized Deconstruct takes its arity from the primary constructor, so a trailing
        // DEFAULT does not save a positional pattern -- a pattern resolves by arity. Both arities exist.
        InputEvent up = new InputEvent.MouseUp(3f, 4f, MouseButton.Right, InputModifier.Ctrl);
        InputEvent move = new InputEvent.MouseMove(5f, 6f, MouseButton.Left);

        var matchedUp = up is InputEvent.MouseUp(var ux, var uy, _) && ux == 3f && uy == 4f;
        var matchedMove = move is InputEvent.MouseMove(var mx, var my) && mx == 5f && my == 6f;

        matchedUp.ShouldBeTrue("MouseUp(var x, var y, _) still binds, and to the right pair");
        matchedMove.ShouldBeTrue("MouseMove(var x, var y) still binds, and to the right pair");
    }

    [Fact]
    public void TheNewParametersAreThereWhenAHostStatesThem()
    {
        new InputEvent.MouseUp(0f, 0f, MouseButton.Left, InputModifier.Shift)
            .Modifiers.ShouldBe(InputModifier.Shift);
        new InputEvent.MouseMove(0f, 0f, MouseButton.Middle)
            .Button.ShouldBe(MouseButton.Middle);
    }

    [Fact]
    public void AMoveWithNothingHeldSaysSoAndAHostThatCannotTellLeavesItThatWay()
    {
        new InputEvent.MouseMove(0f, 0f).Button.ShouldBe(MouseButton.None);
        new InputEvent.MouseUp(0f, 0f, MouseButton.Left).Modifiers.ShouldBe(InputModifier.None);
    }

    [Fact]
    public void MouseButtonNoneDidNotTakeZeroFromLeft()
    {
        // Renumbering would change what every already-compiled host means by a left press, and what
        // every value already stored means, to buy nothing.
        ((int)MouseButton.Left).ShouldBe(0);
        ((int)MouseButton.Middle).ShouldBe(1);
        ((int)MouseButton.Right).ShouldBe(2);
        ((int)MouseButton.None).ShouldBe(-1);
        default(MouseButton).ShouldBe(MouseButton.Left);
    }
}
