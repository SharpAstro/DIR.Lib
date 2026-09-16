using System;
using System.Linq;
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
///
/// <para>
/// <c>TextInputHit</c> took the other road at 10.0, a major being the one place it is available: rather
/// than carrying both arities forever it has ONE, with <c>Painted</c> required. Pinned here too, because
/// the shape that caused the incident -- a trailing optional parameter -- is the shape someone adds back
/// without noticing, and in C# it looks like a source-compatible addition.
/// </para>
/// </summary>
public class InputEventCompatibilityTests
{
    [Fact]
    public void TextInputHitHasExactlyOneConstructorAndPaintedIsRequired()
    {
        // The incident was Console.Lib 4.33 calling new HitResult.TextInputHit(field.State) in
        // CellLayout.HitOf against a 9.1 that had just deleted that arity. 9.2 answered by keeping both;
        // 10.0 rebuilds the chain, so it answers by keeping one -- and a surface registering a field now
        // has to SAY where it laid the text down instead of defaulting to a geometry that puts every
        // caret at the start.
        var ctors = typeof(HitResult.TextInputHit)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        ctors.Length.ShouldBe(1, "two arities is what let a caller mean the wrong one");
        ctors[0].GetParameters().Select(p => p.ParameterType)
            .ShouldBe([typeof(TextInputState), typeof(TextInputGeometry)]);
        ctors[0].GetParameters().ShouldAllBe(p => !p.HasDefaultValue,
            "a trailing default is the 9.1 shape, and it reads as source-compatible right up to the throw");
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
