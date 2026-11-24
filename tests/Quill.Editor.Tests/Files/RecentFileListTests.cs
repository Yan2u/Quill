using Quill.Editor.Files;

namespace Quill.Editor.Tests.Files;

/// <summary>M6.4: recent files, most recent first, at most ten, kept across sessions.</summary>
public sealed class RecentFileListTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "quill-tests", Guid.NewGuid().ToString("N"));

    private string Storage => Path.Combine(_folder, "recent.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void FilesGoToTheFrontOnceAndTheListIsCapped()
    {
        var list = new RecentFileList(Storage, capacity: 3);
        list.Add(@"C:\a.md");
        list.Add(@"C:\b.md");
        list.Add(@"C:\A.MD");
        Assert.Equal([@"C:\A.MD", @"C:\b.md"], list.Paths);

        list.Add(@"C:\c.md");
        list.Add(@"C:\d.md");
        Assert.Equal([@"C:\d.md", @"C:\c.md", @"C:\A.MD"], list.Paths);
    }

    [Fact]
    public void TheListSurvivesASession()
    {
        var list = new RecentFileList(Storage);
        list.Add(@"C:\notes\a.md");
        list.Add(@"C:\notes\b.md");
        list.Remove(@"C:\notes\a.md");
        Assert.Equal([@"C:\notes\b.md"], new RecentFileList(Storage).Paths);

        list.Clear();
        Assert.Empty(new RecentFileList(Storage).Paths);
    }

    [Fact]
    public void ABrokenStorageFileIsAnEmptyList()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Storage, "{ not json");
        var list = new RecentFileList(Storage);
        Assert.Empty(list.Paths);
        list.Add(@"C:\a.md");
        Assert.Single(new RecentFileList(Storage).Paths);
    }

    [Fact]
    public void RemoveRangeRemovesMatchesOnceAndIgnoresMalformedPaths()
    {
        var list = new RecentFileList(Storage);
        list.Add(@"C:\notes\a.md");
        list.Add(@"C:\notes\b.md");
        list.Add(@"C:\notes\c.md");

        int changes = 0;
        list.Changed += (_, _) => changes++;

        bool removed = list.RemoveRange([string.Empty, "   ", "bad\0path", @"C:\NOTES\A.MD", @"c:\notes\c.md", @"C:\notes\missing.md"]);
        Assert.True(removed);
        Assert.Equal(1, changes);
        Assert.Equal([@"C:\notes\b.md"], list.Paths);
        Assert.Equal([@"C:\notes\b.md"], new RecentFileList(Storage).Paths);

        Assert.False(list.RemoveRange([@"C:\notes\missing.md", string.Empty]));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void PathsSnapshotRemainsImmutableAcrossMutations()
    {
        var list = new RecentFileList(Storage);
        list.Add(@"C:\notes\a.md");
        var snapshot = list.Paths;

        list.Add(@"C:\notes\b.md");
        list.Clear();

        Assert.Equal([@"C:\notes\a.md"], snapshot);
        Assert.Empty(list.Paths);
    }

    [Fact]
    public void LoadDeduplicatesEntriesAndDropsBlankPaths()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Storage, """["", "  ", "C:\\notes\\a.md", "C:\\NOTES\\A.MD", "C:\\notes\\b.md"]""");
        var list = new RecentFileList(Storage);
        Assert.Equal([@"C:\notes\a.md", @"C:\notes\b.md"], list.Paths);
    }
}
