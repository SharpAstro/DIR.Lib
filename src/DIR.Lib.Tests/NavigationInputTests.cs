using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// <see cref="NavigationInput.Classify"/>: the one rule that turns a key or a mouse button into Back or Forward. Alt alone
/// is the modifier, the buttons act as they go down, and their release is taken with them.
/// </summary>
public class NavigationInputTests
{
    [Fact]
    public void AltLeftIsBackAndAltRightIsForward()
    {
        NavigationInput.Classify(new InputEvent.KeyDown(InputKey.Left, InputModifier.Alt)).ShouldBe(NavigationCommand.Back);
        NavigationInput.Classify(new InputEvent.KeyDown(InputKey.Right, InputModifier.Alt)).ShouldBe(NavigationCommand.Forward);
    }

    [Fact]
    public void TheMouseButtonsActAsTheyGoDown()
    {
        NavigationInput.Classify(new InputEvent.MouseDown(10f, 10f, MouseButton.Back)).ShouldBe(NavigationCommand.Back);
        NavigationInput.Classify(new InputEvent.MouseDown(10f, 10f, MouseButton.Forward)).ShouldBe(NavigationCommand.Forward);
    }

    [Fact]
    public void TheReleaseOfEitherButtonIsSwallowed()
    {
        NavigationInput.Classify(new InputEvent.MouseUp(10f, 10f, MouseButton.Back)).ShouldBe(NavigationCommand.Swallow);
        NavigationInput.Classify(new InputEvent.MouseUp(10f, 10f, MouseButton.Forward)).ShouldBe(NavigationCommand.Swallow);
    }

    [Theory]
    [InlineData(InputModifier.None)]
    [InlineData(InputModifier.Shift)]
    [InlineData(InputModifier.Ctrl)]
    [InlineData(InputModifier.Alt | InputModifier.Shift)]
    [InlineData(InputModifier.Alt | InputModifier.Ctrl)]
    [InlineData(InputModifier.Alt | InputModifier.Ctrl | InputModifier.Shift)]
    public void ArrowKeysWithAnythingButAltAloneAreNotNavigation(InputModifier modifiers)
    {
        NavigationInput.Classify(new InputEvent.KeyDown(InputKey.Left, modifiers)).ShouldBe(NavigationCommand.None);
        NavigationInput.Classify(new InputEvent.KeyDown(InputKey.Right, modifiers)).ShouldBe(NavigationCommand.None);
    }

    [Fact]
    public void AKeyDownWithNoModifiersArgumentIsNotNavigation()
    {
        NavigationInput.Classify(new InputEvent.KeyDown(InputKey.Left)).ShouldBe(NavigationCommand.None);
    }

    [Theory]
    [InlineData(InputKey.Up)]
    [InlineData(InputKey.Down)]
    [InlineData(InputKey.Enter)]
    public void OtherKeysWithAltAreNotNavigation(InputKey key)
    {
        NavigationInput.Classify(new InputEvent.KeyDown(key, InputModifier.Alt)).ShouldBe(NavigationCommand.None);
    }

    [Fact]
    public void TheReleaseOfAKeyIsNotNavigation()
    {
        NavigationInput.Classify(new InputEvent.KeyUp(InputKey.Left, InputModifier.Alt)).ShouldBe(NavigationCommand.None);
    }

    [Theory]
    [InlineData(MouseButton.Left)]
    [InlineData(MouseButton.Middle)]
    [InlineData(MouseButton.Right)]
    [InlineData(MouseButton.None)]
    public void TheOtherButtonsAreNotNavigationDownOrUp(MouseButton button)
    {
        NavigationInput.Classify(new InputEvent.MouseDown(10f, 10f, button)).ShouldBe(NavigationCommand.None);
        NavigationInput.Classify(new InputEvent.MouseUp(10f, 10f, button)).ShouldBe(NavigationCommand.None);
    }

    [Fact]
    public void AMoveOfThePointerIsNotNavigationEvenWithTheBackButtonHeld()
    {
        NavigationInput.Classify(new InputEvent.MouseMove(10f, 10f) { Button = MouseButton.Back }).ShouldBe(NavigationCommand.None);
    }
}
