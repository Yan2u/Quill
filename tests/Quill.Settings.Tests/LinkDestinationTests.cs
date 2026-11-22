using Quill.Settings.Insertion;

namespace Quill.Settings.Tests;

/// <summary>Destinations and text of inserted links and images (06, Q109), and the image folder's macros.</summary>
public sealed class LinkDestinationTests
{
    [Theory]
    [InlineData(@"C:\a\b.png", "file:///C:/a/b.png")]
    [InlineData(@"C:\my pics\图.png", "file:///C:/my pics/图.png")]
    [InlineData(@"\\server\share\b.png", "file://server/share/b.png")]
    public void FileUrlsUseForwardSlashes(string path, string url)
    {
        Assert.Equal(url, LinkDestination.FileUrl(path));
    }

    [Theory]
    [InlineData("assets/a.png", "assets/a.png")]
    [InlineData("file:///C:/my pics/a.png", "<file:///C:/my pics/a.png>")]
    [InlineData("a(1).png", "a(1).png")]
    [InlineData("a).png", "<a).png>")]
    [InlineData("a<b>.png", @"<a\<b\>.png>")]
    [InlineData("图片.png", "图片.png")]
    [InlineData("", "<>")]
    public void WithoutEscapingDestinationsThatWouldBreakAreInAngleBrackets(string destination, string written)
    {
        Assert.Equal(written, LinkDestination.Format(destination, escape: false));
    }

    [Theory]
    [InlineData("assets/a.png", "assets/a.png")]
    [InlineData("file:///C:/my pics/a.png", "file:///C:/my%20pics/a.png")]
    [InlineData("图.png", "%E5%9B%BE.png")]
    [InlineData("a(1).png", "a%281%29.png")]
    [InlineData("😀.png", "%F0%9F%98%80.png")]
    public void EscapingPercentEncodesSpacesAndNonAscii(string destination, string written)
    {
        Assert.Equal(written, LinkDestination.Format(destination, escape: true));
    }

    [Fact]
    public void LinkTextEscapesBrackets()
    {
        Assert.Equal(@"[a\[1\] \\ b](x.md)", LinkDestination.Markdown(@"a[1] \ b", "x.md", image: false, escape: false));
        Assert.Equal("![shot](./assets/shot.png)", LinkDestination.Markdown("shot", "./assets/shot.png", image: true, escape: false));
    }

    [Fact]
    public void MacrosExpandForTheDocument()
    {
        string document = @"C:\notes\week 1\plan.v2.md";
        Assert.Equal(@"C:\notes\week 1\img\plan.v2.md\plan.v2\week 1",
            ImageFolderMacros.Expand(@"${dirPath}\img\${fileName}\${fileNameNoExt}\${dirName}", document));
        Assert.Equal("./${unknown}/plan.v2", ImageFolderMacros.Expand("./${unknown}/${FILENAMENOEXT}", document));
        Assert.True(ImageFolderMacros.UsesMacros("./${fileName}"));
        Assert.False(ImageFolderMacros.UsesMacros(@"D:\images"));
    }
}
