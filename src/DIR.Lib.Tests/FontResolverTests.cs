using DIR.Lib;
using Shouldly;

namespace DIR.Lib.Tests;

public sealed class FontResolverTests
{
    [Fact]
    public void ResolveSystemFont_Returns_EmptyOrExistingFile()
    {
        var path = FontResolver.ResolveSystemFont();
        // Either nothing was found (empty) or the returned path exists on disk.
        if (path.Length > 0)
            File.Exists(path).ShouldBeTrue($"resolver returned non-empty path \"{path}\" but no file exists there");
    }

    [Fact]
    public void ResolveSystemFont_DoesNotThrow_OnAnyOs()
    {
        Should.NotThrow(() => FontResolver.ResolveSystemFont());
    }
}
