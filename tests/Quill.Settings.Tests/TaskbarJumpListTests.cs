using System.Runtime.InteropServices;
using Quill.Settings.Taskbar;

namespace Quill.Settings.Tests;

/// <summary>CLI launch argument quoting/parsing, Jump List sync rules, and in-memory COM Shell link verification.</summary>
public sealed class TaskbarJumpListTests
{
    private static readonly JumpListLabels DefaultLabels = new(
        RecentFoldersTitle: "Recent Folders",
        RecentFilesTitle: "Recent",
        NewWindowTitle: "New Window",
        NewWindowDescription: "Open a new Quill window");

    [Fact]
    public void Parse_RecognizesAllFlagsAndPositionalPaths()
    {
        Assert.Equal(new LaunchRequest(LaunchActionKind.Default), LaunchArguments.Parse([]));
        Assert.Equal(new LaunchRequest(LaunchActionKind.Default), LaunchArguments.Parse(["Quill.exe"]));
        Assert.Equal(new LaunchRequest(LaunchActionKind.Default), LaunchArguments.Parse(["Quill.exe", "   "]));

        Assert.Equal(
            new LaunchRequest(LaunchActionKind.NewWindow),
            LaunchArguments.Parse(["Quill.exe", "--new-window"]));

        Assert.Equal(
            new LaunchRequest(LaunchActionKind.OpenFile, @"C:\docs\a.md"),
            LaunchArguments.Parse(["Quill.exe", "--open-file", @"C:\docs\a.md"]));

        Assert.Equal(
            new LaunchRequest(LaunchActionKind.OpenFolder, @"C:\docs\specs"),
            LaunchArguments.Parse(["Quill.exe", "--open-folder", @"C:\docs\specs"]));

        Assert.Equal(
            new LaunchRequest(LaunchActionKind.VerifyStartup, @"C:\temp\report.txt"),
            LaunchArguments.Parse(["Quill.exe", "--verify-startup", @"C:\temp\report.txt"]));

        Assert.Equal(
            new LaunchRequest(LaunchActionKind.OpenPath, @"C:\docs\readme.md"),
            LaunchArguments.Parse(["Quill.exe", @"C:\docs\readme.md"]));

        // Missing value after a known flag must not treat the flag itself as a file path.
        Assert.Equal(new LaunchRequest(LaunchActionKind.Default), LaunchArguments.Parse(["Quill.exe", "--open-file"]));
        Assert.Equal(new LaunchRequest(LaunchActionKind.Default), LaunchArguments.Parse(["Quill.exe", "--open-folder"]));
        Assert.Equal(new LaunchRequest(LaunchActionKind.Default), LaunchArguments.Parse(["Quill.exe", "--verify-startup"]));
    }

    [Theory]
    [InlineData(@"C:\notes\a.md")]
    [InlineData(@"C:\Users\My Name\Desktop\my notes.md")]
    [InlineData(@"C:\Users\19559\Desktop\补习速记.md")]
    [InlineData(@"C:\")]
    [InlineData(@"C:\a ""b""\")]
    [InlineData(@"\\server\share\notes")]
    [InlineData("")]
    public void QuoteAndSplitCommandLine_RoundTripsEdgeCases(string value)
    {
        string quoted = LaunchArguments.Quote(value);
        var parts = LaunchArguments.SplitCommandLine(quoted);
        Assert.Single(parts);
        Assert.Equal(value, parts[0]);
    }

    [Fact]
    public void FormattedArguments_RoundTripViaParseCommandLine()
    {
        var fileReq = LaunchArguments.ParseCommandLine(LaunchArguments.ForOpenFile(@"C:\Users\19559\Desktop\26.10.24 补习速记.md"));
        Assert.Equal(new LaunchRequest(LaunchActionKind.OpenFile, @"C:\Users\19559\Desktop\26.10.24 补习速记.md"), fileReq);

        var folderReq = LaunchArguments.ParseCommandLine(LaunchArguments.ForOpenFolder(@"C:\"));
        Assert.Equal(new LaunchRequest(LaunchActionKind.OpenFolder, @"C:\"), folderReq);

        var newWindowReq = LaunchArguments.ParseCommandLine(LaunchArguments.ForNewWindow());
        Assert.Equal(new LaunchRequest(LaunchActionKind.NewWindow), newWindowReq);
    }

    [Fact]
    public void DisplayNames_HandleFilesFoldersDriveRootsAndUnc()
    {
        Assert.Equal("kickoff-prompt.md", LaunchArguments.GetFileDisplayName(@"C:\dir\kickoff-prompt.md"));
        Assert.Equal("specs", LaunchArguments.GetFolderDisplayName(@"C:\dir\specs\"));
        Assert.Equal(@"C:\", LaunchArguments.GetFolderDisplayName(@"C:\"));
        Assert.Equal("docs", LaunchArguments.GetFolderDisplayName(@"\\server\share\docs\"));
        Assert.Equal(@"\\server\share", LaunchArguments.GetFolderDisplayName(@"\\server\share"));
    }

    [Fact]
    public void Sync_BuildsOrderedCategoriesAndNewWindowTask()
    {
        var backend = new FakeJumpListBackend
        {
            FolderIcon = new JumpListIcon(@"C:\Windows\System32\imageres.dll", -3),
        };
        var jumpList = new TaskbarJumpList(backend);

        var input = new JumpListSyncInput(
            ExecutablePath: @"C:\App\Quill.exe",
            FileIcon: new JumpListIcon(@"C:\App\Assets\Quill-Markdown.ico", 0),
            AppIcon: new JumpListIcon(@"C:\App\Assets\Quill.ico", 0),
            FolderIcon: null,
            RecentFolders: [@"C:\work\specs\", @"C:\work\notes"],
            RecentFiles: [@"C:\work\specs\kickoff-prompt.md", @"C:\work\notes\补习速记.md"],
            Labels: DefaultLabels);

        var result = jumpList.Sync(input);

        Assert.True(result.Succeeded);
        Assert.Empty(result.RemovedFiles);
        Assert.Empty(result.RemovedFolders);
        Assert.Null(result.ErrorCode);

        var session = Assert.Single(backend.Sessions);
        Assert.True(session.Committed);
        Assert.False(session.Aborted);
        Assert.Equal(2, session.AppendedCategories.Count);

        var foldersCategory = session.AppendedCategories[0];
        Assert.Equal("Recent Folders", foldersCategory.Title);
        Assert.Equal(2, foldersCategory.Items.Count);
        Assert.Equal("specs", foldersCategory.Items[0].Title);
        Assert.Equal(@"C:\work\specs", foldersCategory.Items[0].Description);
        Assert.Equal(LaunchArguments.ForOpenFolder(@"C:\work\specs"), foldersCategory.Items[0].Arguments);
        Assert.Equal(backend.FolderIcon, foldersCategory.Items[0].Icon);

        var filesCategory = session.AppendedCategories[1];
        Assert.Equal("Recent", filesCategory.Title);
        Assert.Equal(2, filesCategory.Items.Count);
        Assert.Equal("kickoff-prompt.md", filesCategory.Items[0].Title);
        Assert.Equal(@"C:\work\specs\kickoff-prompt.md", filesCategory.Items[0].Description);
        Assert.Equal(LaunchArguments.ForOpenFile(@"C:\work\specs\kickoff-prompt.md"), filesCategory.Items[0].Arguments);
        Assert.Equal(input.FileIcon, filesCategory.Items[0].Icon);
        Assert.Equal("补习速记.md", filesCategory.Items[1].Title);

        var task = Assert.Single(session.AddedTasks);
        Assert.Equal("New Window", task.Title);
        Assert.Equal("Open a new Quill window", task.Description);
        Assert.Equal(LaunchArguments.ForNewWindow(), task.Arguments);
        Assert.Equal(input.AppIcon, task.Icon);
    }

    [Fact]
    public void Sync_FiltersRemovedItemsAndReportsThemForPersistencePruning()
    {
        var backend = new FakeJumpListBackend
        {
            RemovedArguments =
            [
                LaunchArguments.ForOpenFile(@"C:\DOCS\A.MD"),
                LaunchArguments.ForOpenFolder(@"C:\WORK\SPECS\"),
                LaunchArguments.Quote(@"C:\docs\c.md"),
            ],
        };
        var jumpList = new TaskbarJumpList(backend);

        var input = new JumpListSyncInput(
            ExecutablePath: @"C:\App\Quill.exe",
            FileIcon: new JumpListIcon(@"C:\App\Assets\Quill-Markdown.ico"),
            AppIcon: new JumpListIcon(@"C:\App\Assets\Quill.ico"),
            FolderIcon: new JumpListIcon(@"C:\Windows\System32\shell32.dll", 3),
            RecentFolders: [@"C:\work\specs", @"C:\work\other"],
            RecentFiles: [@"C:\docs\a.md", @"C:\docs\b.md", @"C:\docs\c.md"],
            Labels: DefaultLabels);

        var result = jumpList.Sync(input);

        Assert.True(result.Succeeded);
        Assert.Equal([@"C:\DOCS\A.MD", @"C:\docs\c.md"], result.RemovedFiles);
        Assert.Equal([@"C:\WORK\SPECS"], result.RemovedFolders);

        var session = Assert.Single(backend.Sessions);
        Assert.Equal([@"C:\work\other"], session.AppendedCategories[0].Items.Select(static i => i.Description));
        Assert.Equal([@"C:\docs\b.md"], session.AppendedCategories[1].Items.Select(static i => i.Description));
    }

    [Fact]
    public void Sync_OmitsEmptyCategoriesAndStillCommitsTasks()
    {
        var backend = new FakeJumpListBackend
        {
            RemovedArguments = [LaunchArguments.ForOpenFile(@"C:\only.md")],
        };
        var jumpList = new TaskbarJumpList(backend);

        var result = jumpList.Sync(new JumpListSyncInput(
            ExecutablePath: @"C:\App\Quill.exe",
            FileIcon: new JumpListIcon(@"C:\App\Assets\Quill-Markdown.ico"),
            AppIcon: new JumpListIcon(@"C:\App\Assets\Quill.ico"),
            FolderIcon: new JumpListIcon(@"C:\Windows\System32\shell32.dll", 3),
            RecentFolders: [],
            RecentFiles: [@"C:\only.md"],
            Labels: DefaultLabels));

        Assert.True(result.Succeeded);
        var session = Assert.Single(backend.Sessions);
        Assert.Empty(session.AppendedCategories);
        Assert.Single(session.AddedTasks);
        Assert.True(session.Committed);
    }

    [Fact]
    public void Sync_CapsFoldersAtFiveWithFilesAndExpandsToTenWithoutFiles()
    {
        var folders = Enumerable.Range(1, 12).Select(i => $@"C:\folder-{i}").ToList();
        var files = Enumerable.Range(1, 12).Select(i => $@"C:\file-{i}.md").ToList();

        var withFilesBackend = new FakeJumpListBackend();
        new TaskbarJumpList(withFilesBackend).Sync(new JumpListSyncInput(
            @"C:\App\Quill.exe",
            new JumpListIcon("file.ico"),
            new JumpListIcon("app.ico"),
            new JumpListIcon("folder.ico"),
            folders,
            files,
            DefaultLabels));

        var withFilesSession = Assert.Single(withFilesBackend.Sessions);
        Assert.Equal(5, withFilesSession.AppendedCategories[0].Items.Count);
        Assert.Equal(10, withFilesSession.AppendedCategories[1].Items.Count);

        var foldersOnlyBackend = new FakeJumpListBackend();
        new TaskbarJumpList(foldersOnlyBackend).Sync(new JumpListSyncInput(
            @"C:\App\Quill.exe",
            new JumpListIcon("file.ico"),
            new JumpListIcon("app.ico"),
            new JumpListIcon("folder.ico"),
            folders,
            [],
            DefaultLabels));

        var foldersOnlySession = Assert.Single(foldersOnlyBackend.Sessions);
        var onlyCategory = Assert.Single(foldersOnlySession.AppendedCategories);
        Assert.Equal(10, onlyCategory.Items.Count);
    }

    [Fact]
    public void Sync_HandlesBeginFailure_AccessDeniedCategory_CommitFailure_AndExceptions()
    {
        const int beginErr = unchecked((int)0x80004005);
        var noSessionBackend = new FakeJumpListBackend { BeginHResult = beginErr, ReturnNullSession = true };
        var beginResult = new TaskbarJumpList(noSessionBackend).Sync(SimpleInput([], []));
        Assert.False(beginResult.Succeeded);
        Assert.Equal(beginErr, beginResult.ErrorCode);

        const int accessDenied = unchecked((int)0x80070005);
        var deniedBackend = new FakeJumpListBackend { AppendCategoryHResult = accessDenied };
        var deniedResult = new TaskbarJumpList(deniedBackend).Sync(SimpleInput([@"C:\f"], [@"C:\a.md"]));
        Assert.True(deniedResult.Succeeded);
        Assert.Equal(accessDenied, deniedResult.ErrorCode);
        Assert.Single(deniedBackend.Sessions[0].AddedTasks);
        Assert.True(deniedBackend.Sessions[0].Committed);

        const int commitErr = unchecked((int)0x80004001);
        var commitFailBackend = new FakeJumpListBackend { CommitHResult = commitErr };
        var commitResult = new TaskbarJumpList(commitFailBackend).Sync(SimpleInput([], []));
        Assert.False(commitResult.Succeeded);
        Assert.Equal(commitErr, commitResult.ErrorCode);
        Assert.True(commitFailBackend.Sessions[0].Aborted);

        const int comErr = unchecked((int)0x80040154);
        var throwBackend = new FakeJumpListBackend { ThrowOnAppend = new COMException("COM error", comErr) };
        var throwResult = new TaskbarJumpList(throwBackend).Sync(SimpleInput([@"C:\f"], []));
        Assert.False(throwResult.Succeeded);
        Assert.Equal(comErr, throwResult.ErrorCode);
        Assert.True(throwBackend.Sessions[0].Aborted);
    }

    [Fact]
    public void WindowsJumpListBackend_ResolveFolderIcon_ReturnsExistingFileLocation()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows Shell icon lookup requires Windows.");
            return;
        }

        var icon = new WindowsJumpListBackend().ResolveFolderIcon();
        Assert.False(string.IsNullOrWhiteSpace(icon.Path));
        Assert.True(File.Exists(icon.Path), $"Resolved folder icon file does not exist: {icon.Path}");
    }

    [Fact]
    public void WindowsJumpListBackend_RoundTripsShellLinkWithUnicodeAndDriveRootInMemory()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows COM IShellLinkW verification requires Windows.");
            return;
        }

        var fileSpec = new JumpListItemSpec(
            Title: "补习速记.md",
            Description: @"C:\Users\19559\Desktop\补习速记.md",
            ExecutablePath: @"C:\Program Files\Quill\Quill.exe",
            Arguments: LaunchArguments.ForOpenFile(@"C:\Users\19559\Desktop\补习速记.md"),
            Icon: new JumpListIcon(@"C:\Program Files\Quill\Assets\Quill-Markdown.ico", 0));

        var fileRoundTrip = WindowsJumpListBackend.RoundTripShellLinkInMemory(fileSpec);
        Assert.Equal(fileSpec, fileRoundTrip);

        var folderSpec = new JumpListItemSpec(
            Title: @"C:\",
            Description: @"C:\",
            ExecutablePath: @"C:\Program Files\Quill\Quill.exe",
            Arguments: LaunchArguments.ForOpenFolder(@"C:\"),
            Icon: new JumpListIcon(@"C:\Windows\System32\imageres.dll", -3));

        var folderRoundTrip = WindowsJumpListBackend.RoundTripShellLinkInMemory(folderSpec);
        Assert.Equal(folderSpec, folderRoundTrip);
    }

    private static JumpListSyncInput SimpleInput(IReadOnlyList<string> folders, IReadOnlyList<string> files) => new(
        @"C:\App\Quill.exe",
        new JumpListIcon("file.ico"),
        new JumpListIcon("app.ico"),
        new JumpListIcon("folder.ico"),
        folders,
        files,
        DefaultLabels);

    private sealed class FakeJumpListBackend : IJumpListBackend
    {
        public JumpListIcon FolderIcon { get; init; } = new(@"C:\Windows\System32\shell32.dll", 3);

        public IReadOnlyList<string> RemovedArguments { get; init; } = [];

        public bool ReturnNullSession { get; init; }

        public int BeginHResult { get; init; }

        public int AppendCategoryHResult { get; init; }

        public int CommitHResult { get; init; }

        public Exception? ThrowOnAppend { get; init; }

        public List<FakeJumpListSession> Sessions { get; } = [];

        public JumpListIcon ResolveFolderIcon() => FolderIcon;

        public IJumpListSession? TryBeginSession(out int hresult)
        {
            hresult = BeginHResult;
            if (ReturnNullSession)
            {
                return null;
            }

            var session = new FakeJumpListSession(RemovedArguments, AppendCategoryHResult, CommitHResult, ThrowOnAppend);
            Sessions.Add(session);
            return session;
        }
    }

    private sealed class FakeJumpListSession(
        IReadOnlyList<string> removedArguments,
        int appendCategoryHResult,
        int commitHResult,
        Exception? throwOnAppend) : IJumpListSession
    {
        public uint MinSlots => 10;

        public IReadOnlyList<string> RemovedArguments { get; } = removedArguments;

        public List<JumpListCategorySpec> AppendedCategories { get; } = [];

        public List<JumpListItemSpec> AddedTasks { get; } = [];

        public bool Committed { get; private set; }

        public bool Aborted { get; private set; }

        public bool TryAppendCategory(JumpListCategorySpec category, out int hresult)
        {
            if (throwOnAppend is not null)
            {
                throw throwOnAppend;
            }

            hresult = appendCategoryHResult;
            if (appendCategoryHResult < 0)
            {
                return false;
            }

            AppendedCategories.Add(category);
            return true;
        }

        public bool TryAddUserTasks(IReadOnlyList<JumpListItemSpec> tasks, out int hresult)
        {
            AddedTasks.AddRange(tasks);
            hresult = 0;
            return true;
        }

        public bool TryCommit(out int hresult)
        {
            hresult = commitHResult;
            if (commitHResult < 0)
            {
                return false;
            }

            Committed = true;
            return true;
        }

        public void Abort() => Aborted = true;

        public void Dispose()
        {
        }
    }
}
