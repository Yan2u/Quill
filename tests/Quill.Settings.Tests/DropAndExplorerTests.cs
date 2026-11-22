using Quill.Settings.Explorer;
using Quill.Settings.Insertion;

namespace Quill.Settings.Tests;

/// <summary>Dropping files and folders (14-settings.md, Q108), and File Explorer's New menu over a registry in memory.</summary>
public sealed class DropAndExplorerTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static bool IsMarkdown(string path) => Path.GetExtension(path) is ".md" or ".txt";

    private (string Folder, string Markdown, string Image, string Other) Items()
    {
        string folder = Directory.CreateDirectory(Path.Combine(_folder.Path, "notes")).FullName;
        string markdown = _folder.File("a.md");
        string image = _folder.File("b.png");
        string other = _folder.File("c.pdf");
        foreach (string file in new[] { markdown, image, other })
        {
            File.WriteAllText(file, "x");
        }

        return (folder, markdown, image, other);
    }

    [Fact]
    public void ByDefaultFoldersAndMarkdownFilesOpenAndImagesAreInserted()
    {
        var (folder, markdown, image, other) = Items();
        var plan = DropPlanner.Plan([folder, markdown, image, other], [], AppSettings.Default, IsMarkdown);
        Assert.Equal(folder, plan.FolderToOpen);
        Assert.Equal([markdown], plan.FilesToOpen);
        Assert.Equal([new InsertedItem(InsertedKind.LocalImage, image)], plan.Inserted);
    }

    [Fact]
    public void EverythingCanBeLinked()
    {
        var (folder, markdown, image, other) = Items();
        var settings = AppSettings.Default with { DropFolder = FolderDropAction.InsertLink, DropMarkdown = MarkdownDropAction.InsertLink, DropOtherFile = OtherFileDropAction.InsertLink };
        var plan = DropPlanner.Plan([folder, markdown, image, other], [], settings, IsMarkdown);
        Assert.Null(plan.FolderToOpen);
        Assert.Empty(plan.FilesToOpen);
        Assert.Equal(
            [new InsertedItem(InsertedKind.Link, folder), new InsertedItem(InsertedKind.Link, markdown), new InsertedItem(InsertedKind.LocalImage, image), new InsertedItem(InsertedKind.Link, other)],
            plan.Inserted);
    }

    [Fact]
    public void OnlyImageUrlsAreInsertedFromTheWeb()
    {
        var plan = DropPlanner.Plan([], [new Uri("https://example.com/a.png"), new Uri("https://example.com/page"), new Uri("ftp://example.com/b.png")], AppSettings.Default, IsMarkdown);
        Assert.Equal([new InsertedItem(InsertedKind.OnlineImage, "https://example.com/a.png")], plan.Inserted);
    }

    [Fact]
    public async Task InsertedItemsAreWrittenInOrder()
    {
        var (folder, _, image, other) = Items();
        var settings = AppSettings.Default with { DropFolder = FolderDropAction.InsertLink, DropOtherFile = OtherFileDropAction.InsertLink };
        var plan = DropPlanner.Plan([folder, image, other], [], settings, IsMarkdown);
        var images = new ImageInserter(settings, null, _folder.File("images"));
        string markdown = await DropPlanner.WriteAsync(plan.Inserted, images, settings);
        Assert.Equal($"[notes]({LinkDestination.FileUrl(folder)}) ![b]({LinkDestination.FileUrl(image)}) [c.pdf]({LinkDestination.FileUrl(other)})", markdown);
    }

    private const string UserMd = @"HKEY_CURRENT_USER\Software\Classes\.md";

    [Fact]
    public void TheNewMenuIsAddedUnderTheExistingFileType()
    {
        var registry = new MemoryRegistry();
        registry.SetValue(@"HKEY_CLASSES_ROOT\.md", null, "VSCode.md");
        var menu = new ExplorerNewMenu(registry);
        Assert.Equal(NewMenuState.NotAdded, menu.State);

        menu.Add();
        Assert.Equal(NewMenuState.AddedByQuill, menu.State);
        Assert.Equal(string.Empty, registry.GetValue(UserMd + @"\ShellNew", "NullFile"));
        Assert.Null(registry.GetValue(UserMd, null));
        Assert.Equal(1, registry.Notifications);

        menu.Remove();
        Assert.Equal(NewMenuState.NotAdded, menu.State);
        Assert.False(registry.KeyExists(UserMd));
        Assert.Equal("VSCode.md", registry.GetValue(@"HKEY_CLASSES_ROOT\.md", null));
        Assert.Equal(2, registry.Notifications);
    }

    [Fact]
    public void WithoutAFileTypeQuillAddsOneAndRemovesItAgain()
    {
        var registry = new MemoryRegistry();
        var menu = new ExplorerNewMenu(registry);
        menu.Add();
        Assert.Equal(ExplorerNewMenu.ProgId, registry.GetValue(UserMd, null));
        Assert.Equal(ExplorerNewMenu.TypeName, registry.GetValue(@"HKEY_CURRENT_USER\Software\Classes\" + ExplorerNewMenu.ProgId, null));

        menu.Remove();
        Assert.Empty(registry.Keys.Where(static key => key.Contains(".md", StringComparison.OrdinalIgnoreCase) || key.Contains(ExplorerNewMenu.ProgId, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void RemovingKeepsWhatOthersWrote()
    {
        var registry = new MemoryRegistry();
        var menu = new ExplorerNewMenu(registry);
        menu.Add();

        // Another program takes .md meanwhile and keeps a value of its own there.
        registry.SetValue(UserMd, null, "Other.md");
        registry.SetValue(UserMd, "Content Type", "text/markdown");
        menu.Remove();
        Assert.Equal("Other.md", registry.GetValue(UserMd, null));
        Assert.Equal("text/markdown", registry.GetValue(UserMd, "Content Type"));
        Assert.False(registry.KeyExists(UserMd + @"\ShellNew"));
    }

    [Theory]
    [InlineData(@"HKEY_CURRENT_USER\Software\Classes\.md\ShellNew")]
    [InlineData(@"HKEY_CLASSES_ROOT\.md\ShellNew")]
    [InlineData(@"HKEY_CLASSES_ROOT\.md\VSCode.md\ShellNew")]
    public void AnEntryAddedElsewhereIsLeftAlone(string key)
    {
        var registry = new MemoryRegistry();
        registry.SetValue(@"HKEY_CLASSES_ROOT\.md", null, "VSCode.md");
        registry.SetValue(key, "NullFile", string.Empty);
        var menu = new ExplorerNewMenu(registry);
        Assert.Equal(NewMenuState.AddedElsewhere, menu.State);
        menu.Add();
        menu.Remove();
        Assert.True(registry.KeyExists(key));
        Assert.Equal(0, registry.Notifications);
    }
}
