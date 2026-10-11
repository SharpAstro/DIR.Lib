using Shouldly;

namespace DIR.Lib.Tests;

/// <summary>
/// The rules every <see cref="IFileDialogs"/> implementation shares, which live in DIR.Lib so they are
/// written once: how a filter's extensions read, and the extension a save path is given when the user typed
/// none (Windows' <c>lpstrDefExt</c>, which GTK's portal, zenity and macOS do not do).
/// </summary>
public class FileDialogFilterTests
{
    private static readonly FileDialogFilter[] Images =
    [
        new("PNG image", "png"),
        new("JPEG image", "jpg;jpeg"),
        new("TIFF image", "tif;tiff"),
    ];

    [Fact]
    public void ExtensionListSplitsTrimsAndDropsDots() =>
        new FileDialogFilter("FITS", " fits; .fit ;fts;;fz ").ExtensionList.ShouldBe(["fits", "fit", "fts", "fz"]);

    [Fact]
    public void ASaveNameWithoutAnExtensionGetsTheFirstFiltersFirstOne() =>
        FileDialogFilter.WithDefaultExtension(@"C:\out\m42", Images).ShouldBe(@"C:\out\m42.png");

    [Fact]
    public void TheChosenFilterWinsWhenThePlatformSaysWhichItWas() =>
        FileDialogFilter.WithDefaultExtension("/home/me/m42", Images, filterIndex: 1).ShouldBe("/home/me/m42.jpg");

    [Fact]
    public void AnUnknownFilterIndexFallsBackToTheFirst()
    {
        FileDialogFilter.WithDefaultExtension("/home/me/m42", Images, filterIndex: -1).ShouldBe("/home/me/m42.png");
        FileDialogFilter.WithDefaultExtension("/home/me/m42", Images, filterIndex: 7).ShouldBe("/home/me/m42.png");
    }

    [Fact]
    public void AnExtensionTheUserTypedIsKept()
    {
        // Even one the filters do not list: the user said what they wanted, and the caller reads the format
        // from the extension, so it can refuse one it does not write.
        FileDialogFilter.WithDefaultExtension("/home/me/m42.tif", Images).ShouldBe("/home/me/m42.tif");
        FileDialogFilter.WithDefaultExtension("/home/me/m42.webp", Images).ShouldBe("/home/me/m42.webp");
    }

    [Fact]
    public void AMatchEverythingFilterHasNoExtensionToGive() =>
        FileDialogFilter.WithDefaultExtension("/home/me/notes", [new("All files", "*")]).ShouldBe("/home/me/notes");

    [Fact]
    public void NoFiltersLeavesThePathAlone() =>
        FileDialogFilter.WithDefaultExtension("/home/me/notes", []).ShouldBe("/home/me/notes");

    [Fact]
    public void AnEmptyResultIsACancelAndNotAnError()
    {
        var cancelled = new FileDialogResult();
        cancelled.Cancelled.ShouldBeTrue();
        cancelled.Path.ShouldBeNull();
        cancelled.FilterIndex.ShouldBe(-1);

        var failed = new FileDialogResult { Error = "no portal" };
        failed.Cancelled.ShouldBeFalse();

        var chosen = new FileDialogResult { Paths = ["/a.fits", "/b.fits"] };
        chosen.Cancelled.ShouldBeFalse();
        chosen.Path.ShouldBe("/a.fits");
    }
}
