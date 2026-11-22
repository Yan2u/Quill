using Quill.Settings.Insertion;

namespace Quill.Settings.Tests;

/// <summary>The image rule (14-settings.md, Q109) over real files in a temporary folder.</summary>
public sealed class ImageInserterTests : IDisposable
{
    private static readonly byte[] Red = [1, 2, 3];
    private static readonly byte[] Blue = [4, 5, 6, 7];
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 15, 30, 45, 123, TimeSpan.Zero);

    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private string Document => Path.Combine(_folder.Path, "docs", "plan.md");

    private string Images => Path.Combine(_folder.Path, "app-images");

    private string Picture(string name = "shot one.png", byte[]? content = null)
    {
        string path = Path.Combine(_folder.Path, "pictures", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content ?? Red);
        return path;
    }

    private ImageInserter Inserter(AppSettings settings, string? document = "", Func<Uri, CancellationToken, Task<DownloadedImage>>? download = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Document)!);
        return new ImageInserter(settings, document == string.Empty ? Document : document, Images, download, () => Now);
    }

    [Fact]
    public async Task WithoutARuleALocalImageIsItsFileUrl()
    {
        string picture = Picture();
        var image = await Inserter(AppSettings.Default).LocalAsync(picture);
        Assert.Equal(new InsertedImage("shot one", LinkDestination.FileUrl(picture)), image);
        Assert.False(Directory.Exists(Path.Combine(_folder.Path, "docs", "assets")));
    }

    [Theory]
    [InlineData(ImageInsertRule.CopyToCurrentFolder, null, "", "shot one.png")]
    [InlineData(ImageInsertRule.CopyToAssets, null, "assets", "./assets/shot one.png")]
    [InlineData(ImageInsertRule.CopyToFileAssets, null, "plan.assets", "./plan.assets/shot one.png")]
    [InlineData(ImageInsertRule.CopyToFolder, "img/${fileNameNoExt}", @"img\plan", "img/plan/shot one.png")]
    [InlineData(ImageInsertRule.CopyToFolder, "", "", "shot one.png")]
    public async Task ARelativeRuleCopiesTheImageAndInsertsARelativePath(ImageInsertRule rule, string? folder, string copiedTo, string destination)
    {
        string picture = Picture();
        var inserter = Inserter(AppSettings.Default with { ImageRule = rule, ImageFolder = folder });
        var image = await inserter.LocalAsync(picture);
        Assert.Equal(destination, image.Destination);
        Assert.Equal(Red, File.ReadAllBytes(Path.Combine(_folder.Path, "docs", copiedTo, "shot one.png")));
        Assert.Equal("![shot one](<./assets/shot one.png>)", new ImageInserter(AppSettings.Default, Document, Images).Markdown(image with { Destination = "./assets/shot one.png" }));
    }

    [Fact]
    public async Task AnAbsoluteRuleInsertsAFileUrl()
    {
        string target = Path.Combine(_folder.Path, "store");
        var inserter = Inserter(AppSettings.Default with { ImageRule = ImageInsertRule.CopyToFolder, ImageFolder = target });
        var image = await inserter.LocalAsync(Picture());
        Assert.Equal(LinkDestination.FileUrl(Path.Combine(target, "shot one.png")), image.Destination);
    }

    [Fact]
    public async Task MacrosMakeAnAbsoluteFolderFromTheDocument()
    {
        var inserter = Inserter(AppSettings.Default with { ImageRule = ImageInsertRule.CopyToFolder, ImageFolder = @"${dirPath}\pics" });
        var image = await inserter.LocalAsync(Picture());
        Assert.Equal(LinkDestination.FileUrl(Path.Combine(_folder.Path, "docs", "pics", "shot one.png")), image.Destination);
    }

    [Fact]
    public async Task TheRuleCanBeOffForLocalImages()
    {
        string picture = Picture();
        var image = await Inserter(AppSettings.Default with { ImageRule = ImageInsertRule.CopyToAssets, ApplyImageRuleToLocal = false }).LocalAsync(picture);
        Assert.Equal(LinkDestination.FileUrl(picture), image.Destination);
    }

    [Fact]
    public async Task AFileOfTheSameNameGetsANumberUnlessItHasTheSameContent()
    {
        var inserter = Inserter(AppSettings.Default with { ImageRule = ImageInsertRule.CopyToAssets });
        Assert.Equal("./assets/a.png", (await inserter.LocalAsync(Picture("a.png", Red))).Destination);
        Assert.Equal("./assets/a.png", (await inserter.LocalAsync(Picture("a.png", Red))).Destination);
        Assert.Equal("./assets/a-1.png", (await inserter.LocalAsync(Picture("a.png", Blue))).Destination);
        Assert.Equal("./assets/a-1.png", (await inserter.LocalAsync(Picture("a.png", Blue))).Destination);
        Assert.Equal(Blue, File.ReadAllBytes(Path.Combine(_folder.Path, "docs", "assets", "a-1.png")));
    }

    [Fact]
    public async Task AnImageAlreadyInTheFolderIsNotCopied()
    {
        string inside = Path.Combine(_folder.Path, "docs", "assets", "x.png");
        Directory.CreateDirectory(Path.GetDirectoryName(inside)!);
        File.WriteAllBytes(inside, Red);
        var image = await Inserter(AppSettings.Default with { ImageRule = ImageInsertRule.CopyToAssets }).LocalAsync(inside);
        Assert.Equal("./assets/x.png", image.Destination);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(inside)!));
    }

    [Fact]
    public async Task AnUntitledDocumentKeepsImagesInTheAppFolder()
    {
        var image = await Inserter(AppSettings.Default with { ImageRule = ImageInsertRule.CopyToAssets }, document: null).LocalAsync(Picture());
        Assert.Equal(LinkDestination.FileUrl(Path.Combine(Images, "shot one.png")), image.Destination);

        string target = Path.Combine(_folder.Path, "store");
        var absolute = await Inserter(AppSettings.Default with { ImageRule = ImageInsertRule.CopyToFolder, ImageFolder = target }, document: null).LocalAsync(Picture());
        Assert.Equal(LinkDestination.FileUrl(Path.Combine(target, "shot one.png")), absolute.Destination);
    }

    [Fact]
    public async Task ImageDataIsSavedUnderATimeStamp()
    {
        var plain = await Inserter(AppSettings.Default).DataAsync(Red);
        Assert.Equal(new InsertedImage("image-20261103153045123", LinkDestination.FileUrl(Path.Combine(Images, "image-20261103153045123.png"))), plain);

        var ruled = await Inserter(AppSettings.Default with { ImageRule = ImageInsertRule.CopyToAssets }).DataAsync(Blue);
        Assert.Equal("./assets/image-20261103153045123.png", ruled.Destination);
    }

    [Fact]
    public async Task OnlineImagesKeepTheirUrlUnlessTheRuleAppliesToThem()
    {
        var url = new Uri("https://example.com/pics/cat%20one.png?size=2");
        int downloads = 0;
        Task<DownloadedImage> Download(Uri _, CancellationToken __)
        {
            downloads++;
            return Task.FromResult(new DownloadedImage(Red, "image/png"));
        }

        var kept = await Inserter(AppSettings.Default with { ImageRule = ImageInsertRule.CopyToAssets }, download: Download).OnlineAsync(url);
        Assert.Equal(new InsertedImage("cat one", url.OriginalString), kept);
        Assert.Equal(0, downloads);

        var saved = await Inserter(AppSettings.Default with { ImageRule = ImageInsertRule.CopyToAssets, ApplyImageRuleToOnline = true }, download: Download).OnlineAsync(url);
        Assert.Equal("./assets/cat one.png", saved.Destination);
        Assert.Equal(1, downloads);
    }

    [Fact]
    public async Task ADownloadNamesTheFileByItsTypeAndAFailureKeepsTheUrl()
    {
        var settings = AppSettings.Default with { ImageRule = ImageInsertRule.CopyToAssets, ApplyImageRuleToOnline = true };
        var typed = await Inserter(settings, download: (_, _) => Task.FromResult(new DownloadedImage(Red, "image/jpeg"))).OnlineAsync(new Uri("https://example.com/render"));
        Assert.Equal("./assets/render.jpg", typed.Destination);

        var url = new Uri("https://example.com/a.png");
        var failed = await Inserter(settings, download: (_, _) => throw new HttpRequestException("offline")).OnlineAsync(url);
        Assert.Equal(url.OriginalString, failed.Destination);
    }
}
