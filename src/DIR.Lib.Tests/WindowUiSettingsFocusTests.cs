using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// Focus is a per-WINDOW fact, so it rides on <see cref="WindowUiSettings"/> with the DPI scale and the
/// fonts and is shared the same way.
///
/// <para>
/// What this pins is the failure that happens when it is NOT shared. A window whose fields sit in more
/// than one widget -- a search box on a panel, an editable readout in the chrome -- gives each widget its
/// own owner unless something says otherwise, and then both believe they hold the keyboard. On screen
/// that is two carets blinking, one of which is dead; in code it is a Tab that cycles the wrong list and
/// a blur that never fires. Nothing throws, so the only way to notice is to look.
/// </para>
/// </summary>
public class WindowUiSettingsFocusTests
{
    private sealed class Panel(Renderer<RgbaImage> renderer) : PixelWidgetBase<RgbaImage>(renderer);

    /// <summary>A composite that hands its own context to the panels it hosts, as a window's chrome does.</summary>
    private sealed class Chrome : PixelWidgetBase<RgbaImage>
    {
        public Panel Left { get; }
        public Panel Right { get; }

        public Chrome(Renderer<RgbaImage> renderer) : base(renderer)
        {
            Left = new Panel(renderer);
            Right = new Panel(renderer);
            ShareUiContext(Left, Right);
        }
    }

    /// <summary>The same composite with the sharing left out -- the state this is all here to rule out.</summary>
    private sealed class UnsharedChrome : PixelWidgetBase<RgbaImage>
    {
        public Panel Left { get; }

        public UnsharedChrome(Renderer<RgbaImage> renderer) : base(renderer)
            => Left = new Panel(renderer);
    }

    private static RgbaImageRenderer Renderer() => new(64, 64);

    [Fact]
    public void SharingTheContextSharesTheOneKeyboard()
    {
        var chrome = new Chrome(Renderer());

        chrome.Ui.Focus.ShouldBeSameAs(chrome.Left.Ui.Focus);
        chrome.Ui.Focus.ShouldBeSameAs(chrome.Right.Ui.Focus);
    }

    [Fact]
    public void FocusingInOnePanelBlursTheFieldInTheOther()
    {
        var chrome = new Chrome(Renderer());
        var search = new TextInputState();
        var readout = new TextInputState();

        chrome.Left.Ui.Focus.Focus(search);
        search.IsActive.ShouldBeTrue();

        // The whole point of one owner per window: taking the keyboard anywhere in the window releases it
        // everywhere else, so there is never a second field that looks focused and takes no input.
        chrome.Right.Ui.Focus.Focus(readout);

        search.IsActive.ShouldBeFalse();
        readout.IsActive.ShouldBeTrue();
        chrome.Ui.Focus.Current.ShouldBeSameAs(readout);
    }

    [Fact]
    public void WithoutSharingEachWidgetBelievesItHoldsTheKeyboard()
    {
        var chrome = new UnsharedChrome(Renderer());
        var search = new TextInputState();
        var readout = new TextInputState();

        chrome.Ui.Focus.Focus(readout);
        chrome.Left.Ui.Focus.Focus(search);

        // Two live fields, two owners, and neither blurs the other. Asserted rather than merely described,
        // because this is what a consumer gets by simply not knowing the context has to be shared -- and it
        // is indistinguishable from working until someone types.
        readout.IsActive.ShouldBeTrue();
        search.IsActive.ShouldBeTrue();
        chrome.Ui.Focus.ShouldNotBeSameAs(chrome.Left.Ui.Focus);
    }

    [Fact]
    public void TheHostBindsThePlatformLifecycleOnceForTheWholeWindow()
    {
        var chrome = new Chrome(Renderer());
        var transitions = 0;
        chrome.Ui.Focus.FocusChanged += (_, _) => transitions++;

        var a = new TextInputState();
        var b = new TextInputState();

        // A single subscription sees every move in the window, including ones made from a child panel --
        // which is what lets a host start and stop the platform's text input (and its IME) in one place
        // instead of in each widget that owns a field.
        chrome.Left.Ui.Focus.Focus(a);
        chrome.Right.Ui.Focus.Focus(b);
        chrome.Ui.Focus.Blur();

        transitions.ShouldBe(3);
    }
}
