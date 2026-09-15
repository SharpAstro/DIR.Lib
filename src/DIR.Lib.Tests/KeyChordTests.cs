using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// The precedence rule a host otherwise answers per key, at the one site that noticed.
///
/// <para>
/// The shape this replaces: a key router carrying <c>if (key == F3) return false;</c> and a comment
/// saying F3 is global, beside a hand-written Ctrl+letter map that is not; a bare <c>F</c> meaning a
/// filter cycle in one panel and a typed letter in another; and three copies of the whole arrangement,
/// one per surface. None of them could STATE the rule, because there was nowhere to put it.
/// </para>
/// </summary>
public class KeyChordTests
{
    [Theory]
    [InlineData(InputKey.F, InputModifier.Ctrl)]
    [InlineData(InputKey.S, InputModifier.Ctrl | InputModifier.Shift)]
    [InlineData(InputKey.Enter, InputModifier.Alt)]
    public void ACtrlOrAltChordReachesItsNodeWhileAFieldHasTheKeyboard(InputKey key, InputModifier mods)
    {
        // The user's own example: Ctrl+F reaches the search box while you are typing in another field.
        new KeyChord(key, mods).BeatsFocusedField.ShouldBeTrue();
    }

    [Theory]
    [InlineData(InputKey.F1)]
    [InlineData(InputKey.F3)]
    [InlineData(InputKey.F12)]
    public void AFunctionKeyDoesTooAndNeedsNoSpecialCase(InputKey key)
    {
        // This is the F3 a host used to name in an if-statement of its own.
        new KeyChord(key).BeatsFocusedField.ShouldBeTrue();
    }

    [Theory]
    [InlineData(InputKey.F, InputModifier.None)]
    [InlineData(InputKey.F, InputModifier.Shift)]
    [InlineData(InputKey.Escape, InputModifier.None)]
    public void ABareLetterOrShiftLetterDoesNotAndStaysATypedCharacter(InputKey key, InputModifier mods)
    {
        new KeyChord(key, mods).BeatsFocusedField.ShouldBeFalse();
    }

    [Fact]
    public void ADefaultChordIsOneThatCanNeverFire()
    {
        default(KeyChord).Key.ShouldBe(InputKey.None);
        default(KeyChord).Modifiers.ShouldBe(InputModifier.None);
        default(KeyChord).BeatsFocusedField.ShouldBeFalse();
    }

    [Fact]
    public void ModifiersAreMatchedWholeRatherThanAsASubset()
    {
        // Ctrl+Shift+F is a different binding from Ctrl+F. A host that reports the extra bit means it,
        // and a subset test would fire the wrong one of two bindings that differ only by Shift.
        new KeyChord(InputKey.F, InputModifier.Ctrl)
            .ShouldNotBe(new KeyChord(InputKey.F, InputModifier.Ctrl | InputModifier.Shift));
        new KeyChord(InputKey.F, InputModifier.Ctrl).ShouldBe(new KeyChord(InputKey.F, InputModifier.Ctrl));
    }
}
