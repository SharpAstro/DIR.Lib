using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="InputEvent.MouseMove"/> carries the modifiers held while moving (11.0), and the shapes
/// written against it before keep working: the two- and three-argument constructors (a defaulted record
/// parameter would otherwise delete them from the assembly, which is binary-fatal for a compiled host)
/// and the two- and three-element positional patterns.
/// </summary>
public class MouseMoveModifiersTests
{
    [Fact]
    public void AMoveCarriesTheModifiersHeldWhileMoving()
    {
        var move = new InputEvent.MouseMove(10f, 20f, MouseButton.None, InputModifier.Ctrl);

        move.Modifiers.ShouldBe(InputModifier.Ctrl);
    }

    [Fact]
    public void TheEarlierConstructorsStillBindAndMeanNoModifiers()
    {
        new InputEvent.MouseMove(10f, 20f).Modifiers.ShouldBe(InputModifier.None);

        var withButton = new InputEvent.MouseMove(10f, 20f, MouseButton.Left);
        withButton.Button.ShouldBe(MouseButton.Left);
        withButton.Modifiers.ShouldBe(InputModifier.None);
    }

    [Fact]
    public void TheEarlierPositionalPatternsStillMatch()
    {
        InputEvent evt = new InputEvent.MouseMove(10f, 20f, MouseButton.Left, InputModifier.Shift);

        (evt is InputEvent.MouseMove(var x2, var y2)).ShouldBeTrue();
        (evt is InputEvent.MouseMove(var x3, var y3, var button) && button == MouseButton.Left).ShouldBeTrue();
        (evt is InputEvent.MouseMove(_, _, _, var mods) && mods == InputModifier.Shift).ShouldBeTrue();
    }
}
