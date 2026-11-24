using Quill.Editor.Files;

namespace Quill.Editor.Tests.Files;

/// <summary>Recently opened folders: normalized paths, most recent first, capped, thread-safe, persisted.</summary>
public sealed class RecentFolderListTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "quill-tests", Guid.NewGuid().ToString("N"));

    private string Storage => Path.Combine(_folder, "recent-folders.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void FoldersNormalizeTrailingSeparatorsAndPreserveDriveRoots()
    {
        var list = new RecentFolderList(Storage);
        list.Add(@"C:\notes\specs\");
        list.Add(@"C:\other");
        list.Add(@"c:\notes\specs");
        list.Add(@"C:\");

        Assert.Equal([@"C:\", @"c:\notes\specs", @"C:\other"], list.Paths);
        Assert.Equal([@"C:\", @"c:\notes\specs", @"C:\other"], new RecentFolderList(Storage).Paths);
    }

    [Fact]
    public void FoldersCapAtCapacityAndPersistAcrossSessions()
    {
        var list = new RecentFolderList(Storage, capacity: 3);
        list.Add(@"C:\one");
        list.Add(@"C:\two");
        list.Add(@"C:\three");
        list.Add(@"C:\four");

        Assert.Equal([@"C:\four", @"C:\three", @"C:\two"], list.Paths);
        Assert.Equal([@"C:\four", @"C:\three", @"C:\two"], new RecentFolderList(Storage, capacity: 3).Paths);

        list.Remove(@"C:\three\");
        Assert.Equal([@"C:\four", @"C:\two"], new RecentFolderList(Storage).Paths);

        list.Clear();
        Assert.Empty(new RecentFolderList(Storage).Paths);
    }

    [Fact]
    public void RemoveRangeNormalizesTrailingSeparatorsAndRaisesChangedOnce()
    {
        var list = new RecentFolderList(Storage);
        list.Add(@"C:\one");
        list.Add(@"C:\two");
        list.Add(@"C:\three\");

        int changes = 0;
        list.Changed += (_, _) => changes++;

        bool removed = list.RemoveRange([string.Empty, "bad\0folder", @"C:\ONE\", @"C:\three"]);
        Assert.True(removed);
        Assert.Equal(1, changes);
        Assert.Equal([@"C:\two"], list.Paths);
        Assert.Equal([@"C:\two"], new RecentFolderList(Storage).Paths);

        Assert.False(list.RemoveRange([@"C:\missing\"]));
        Assert.Equal(1, changes);
    }

    [Fact]
    public void BrokenStorageFileRecoversGracefully()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Storage, "{ broken");
        var list = new RecentFolderList(Storage);
        Assert.Empty(list.Paths);

        list.Add(@"C:\notes\");
        Assert.Equal([@"C:\notes"], new RecentFolderList(Storage).Paths);
    }

    [Fact]
    public void PathsSnapshotRemainsImmutableAcrossMutations()
    {
        var list = new RecentFolderList(Storage);
        list.Add(@"C:\notes\");
        var snapshot = list.Paths;

        list.Add(@"C:\other");
        list.Clear();

        Assert.Equal([@"C:\notes"], snapshot);
        Assert.Empty(list.Paths);
    }
}
