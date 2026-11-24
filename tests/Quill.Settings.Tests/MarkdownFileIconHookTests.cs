using Quill.Settings.Explorer;

namespace Quill.Settings.Tests;

/// <summary>Tests for the passive Windows Markdown file icon hook over an in-memory registry.</summary>
public sealed class MarkdownFileIconHookTests
{
    private const string UserClasses = @"HKEY_CURRENT_USER\Software\Classes";
    private const string ClassesRoot = @"HKEY_CLASSES_ROOT";
    private const string UserFileExts = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts";

    private const string QuillExe = @"C:\Apps\Quill\Quill.exe";
    private const string QuillIcon = @"C:\Apps\Quill\Assets\Quill-Markdown.ico";
    private const string TyporaExe = @"C:\Program Files\Typora\Typora.exe";

    private static Func<string, bool> ExistingFiles(params string[] paths)
    {
        var set = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        return set.Contains;
    }

    [Fact]
    public void DoesNothingWhenAnotherAppIsDefault()
    {
        var registry = new MemoryRegistry();
        registry.SetValue($@"{UserFileExts}\.md\UserChoiceLatest\ProgId", "ProgId", "Typora.md");
        registry.SetValue($@"{UserFileExts}\.md\UserChoice", "ProgId", "Typora.md");
        registry.SetValue($@"{ClassesRoot}\Typora.md\shell\open\command", null, $"\"{TyporaExe}\" \"%1\"");
        registry.SetValue($@"{ClassesRoot}\Typora.md\DefaultIcon", null, @"C:\Program Files\Typora\resources\assets\file.ico");

        var hook = new MarkdownFileIconHook(registry, ExistingFiles(QuillExe, QuillIcon, TyporaExe));
        var result = hook.SyncIfDefault(QuillExe, QuillIcon);

        Assert.Empty(result.MatchedProgIds);
        Assert.Empty(result.UpdatedProgIds);
        Assert.False(result.Notified);
        Assert.Equal(0, registry.Notifications);
        Assert.False(registry.KeyExists($@"{UserClasses}\Typora.md"));
        Assert.False(registry.KeyExists($@"{UserClasses}\Applications\Quill.exe"));
    }

    [Fact]
    public void DoesNothingWhenOnlyTxtIsAssociatedWithQuill()
    {
        var registry = new MemoryRegistry();
        registry.SetValue($@"{UserFileExts}\.txt\UserChoiceLatest\ProgId", "ProgId", @"Applications\Quill.exe");
        registry.SetValue($@"{UserClasses}\Applications\Quill.exe\shell\open\command", null, $"\"{QuillExe}\" \"%1\"");

        var hook = new MarkdownFileIconHook(registry, ExistingFiles(QuillExe, QuillIcon));
        var result = hook.SyncIfDefault(QuillExe, QuillIcon);

        Assert.Empty(result.MatchedProgIds);
        Assert.Empty(result.UpdatedProgIds);
        Assert.False(result.Notified);
        Assert.Equal(0, registry.Notifications);
        Assert.Null(registry.GetValue($@"{UserClasses}\Applications\Quill.exe\DefaultIcon", null));
    }

    [Fact]
    public void UpdatesDefaultIconWhenUserChoiceLatestPointsToApplicationsQuillExe()
    {
        var registry = new MemoryRegistry();
        registry.SetValue($@"{UserFileExts}\.md\UserChoiceLatest\ProgId", "ProgId", @"Applications\Quill.exe");
        registry.SetValue($@"{UserClasses}\Applications\Quill.exe\shell\open\command", null, $"\"{QuillExe}\" \"%1\"");

        var hook = new MarkdownFileIconHook(registry, ExistingFiles(QuillExe, QuillIcon));
        var result = hook.SyncIfDefault(QuillExe, QuillIcon);

        Assert.Equal([@"Applications\Quill.exe"], result.MatchedProgIds);
        Assert.Equal([@"Applications\Quill.exe"], result.UpdatedProgIds);
        Assert.True(result.Notified);
        Assert.Equal(1, registry.Notifications);
        Assert.Equal($"\"{QuillIcon}\",0", registry.GetValue($@"{UserClasses}\Applications\Quill.exe\DefaultIcon", null));
        Assert.Equal($"\"{QuillExe}\",0", registry.GetValue($@"{UserClasses}\Applications\Quill.exe\shell\open", "Icon"));
    }

    [Fact]
    public void UpdatesDefaultIconWhenLegacyUserChoicePointsToApplicationsQuillExe()
    {
        var registry = new MemoryRegistry();
        registry.SetValue($@"{UserFileExts}\.markdown\UserChoice", "ProgId", @"Applications\Quill.exe");
        registry.SetValue($@"{UserClasses}\Applications\Quill.exe\shell\open\command", null, $"\"{QuillExe}\" \"%1\"");

        var hook = new MarkdownFileIconHook(registry, ExistingFiles(QuillExe, QuillIcon));
        var result = hook.SyncIfDefault(QuillExe, QuillIcon);

        Assert.Equal([@"Applications\Quill.exe"], result.MatchedProgIds);
        Assert.Equal([@"Applications\Quill.exe"], result.UpdatedProgIds);
        Assert.True(result.Notified);
        Assert.Equal(1, registry.Notifications);
        Assert.Equal($"\"{QuillIcon}\",0", registry.GetValue($@"{UserClasses}\Applications\Quill.exe\DefaultIcon", null));
    }

    [Fact]
    public void UserChoiceLatestTakesPrecedenceOverStaleUserChoice()
    {
        var registry = new MemoryRegistry();
        registry.SetValue($@"{UserFileExts}\.md\UserChoiceLatest\ProgId", "ProgId", "Typora.md");
        registry.SetValue($@"{UserFileExts}\.md\UserChoice", "ProgId", @"Applications\Quill.exe");
        registry.SetValue($@"{ClassesRoot}\Typora.md\shell\open\command", null, $"\"{TyporaExe}\" \"%1\"");
        registry.SetValue($@"{UserClasses}\Applications\Quill.exe\shell\open\command", null, $"\"{QuillExe}\" \"%1\"");

        var hook = new MarkdownFileIconHook(registry, ExistingFiles(QuillExe, QuillIcon, TyporaExe));
        var result = hook.SyncIfDefault(QuillExe, QuillIcon);

        Assert.Empty(result.MatchedProgIds);
        Assert.Empty(result.UpdatedProgIds);
        Assert.False(result.Notified);
        Assert.Equal(0, registry.Notifications);
        Assert.Null(registry.GetValue($@"{UserClasses}\Applications\Quill.exe\DefaultIcon", null));
    }

    [Fact]
    public void UpdatesAutoFileProgIdWhenWindowsCreatesOne()
    {
        var registry = new MemoryRegistry();
        registry.SetValue($@"{UserFileExts}\.mkd\UserChoiceLatest\ProgId", "ProgId", "mkd_auto_file");
        registry.SetValue($@"{UserClasses}\mkd_auto_file\shell\open\command", null, $"\"{QuillExe}\" \"%1\"");

        var hook = new MarkdownFileIconHook(registry, ExistingFiles(QuillExe, QuillIcon));
        var result = hook.SyncIfDefault(QuillExe, QuillIcon);

        Assert.Equal(["mkd_auto_file"], result.MatchedProgIds);
        Assert.Equal(["mkd_auto_file"], result.UpdatedProgIds);
        Assert.True(result.Notified);
        Assert.Equal(1, registry.Notifications);
        Assert.Equal($"\"{QuillIcon}\",0", registry.GetValue($@"{UserClasses}\mkd_auto_file\DefaultIcon", null));
        Assert.Equal($"\"{QuillExe}\",0", registry.GetValue($@"{UserClasses}\mkd_auto_file\shell\open", "Icon"));
    }

    [Fact]
    public void FallsBackToExtensionDefaultWhenNoUserChoiceExists()
    {
        var registry = new MemoryRegistry();
        registry.SetValue($@"{UserClasses}\.mdown", null, "mdown_auto_file");
        registry.SetValue($@"{UserClasses}\mdown_auto_file\shell\open\command", null, $"\"{QuillExe}\" \"%1\"");

        var hook = new MarkdownFileIconHook(registry, ExistingFiles(QuillExe, QuillIcon));
        var result = hook.SyncIfDefault(QuillExe, QuillIcon);

        Assert.Equal(["mdown_auto_file"], result.MatchedProgIds);
        Assert.Equal(["mdown_auto_file"], result.UpdatedProgIds);
        Assert.True(result.Notified);
        Assert.Equal($"\"{QuillIcon}\",0", registry.GetValue($@"{UserClasses}\mdown_auto_file\DefaultIcon", null));
    }

    [Fact]
    public void IgnoresExplorerNewMenuBareQuillMdWithoutOpenCommand()
    {
        var registry = new MemoryRegistry();
        new ExplorerNewMenu(registry).Add();

        var hook = new MarkdownFileIconHook(registry, ExistingFiles(QuillExe, QuillIcon));
        var result = hook.SyncIfDefault(QuillExe, QuillIcon);

        Assert.Empty(result.MatchedProgIds);
        Assert.Empty(result.UpdatedProgIds);
        Assert.False(result.Notified);
        Assert.Equal(1, registry.Notifications);
        Assert.Null(registry.GetValue($@"{UserClasses}\{ExplorerNewMenu.ProgId}\DefaultIcon", null));
    }

    [Fact]
    public void IsIdempotentAndSkipsNotifyWhenAlreadyUpToDate()
    {
        var registry = new MemoryRegistry();
        registry.SetValue($@"{UserFileExts}\.md\UserChoiceLatest\ProgId", "ProgId", @"Applications\Quill.exe");
        registry.SetValue($@"{UserClasses}\Applications\Quill.exe\shell\open\command", null, $"\"{QuillExe}\" \"%1\"");

        var hook = new MarkdownFileIconHook(registry, ExistingFiles(QuillExe, QuillIcon));
        var first = hook.SyncIfDefault(QuillExe, QuillIcon);
        var second = hook.SyncIfDefault(QuillExe, QuillIcon);

        Assert.Equal([@"Applications\Quill.exe"], first.UpdatedProgIds);
        Assert.True(first.Notified);
        Assert.Equal([@"Applications\Quill.exe"], second.MatchedProgIds);
        Assert.Empty(second.UpdatedProgIds);
        Assert.False(second.Notified);
        Assert.Equal(1, registry.Notifications);
    }

    [Fact]
    public void DeduplicatesSharedProgIdAcrossMultipleMarkdownExtensions()
    {
        var registry = new MemoryRegistry();
        foreach (string ext in MarkdownFileIconHook.Extensions)
        {
            registry.SetValue($@"{UserFileExts}\{ext}\UserChoiceLatest\ProgId", "ProgId", @"Applications\Quill.exe");
        }

        registry.SetValue($@"{UserClasses}\Applications\Quill.exe\shell\open\command", null, $"\"{QuillExe}\" \"%1\"");

        var hook = new MarkdownFileIconHook(registry, ExistingFiles(QuillExe, QuillIcon));
        var result = hook.SyncIfDefault(QuillExe, QuillIcon);

        Assert.Equal([@"Applications\Quill.exe"], result.MatchedProgIds);
        Assert.Equal([@"Applications\Quill.exe"], result.UpdatedProgIds);
        Assert.Equal(1, registry.Notifications);
    }

    [Fact]
    public void UpdatesMultipleDistinctQuillProgIdsWithSingleNotification()
    {
        var registry = new MemoryRegistry();
        registry.SetValue($@"{UserFileExts}\.md\UserChoiceLatest\ProgId", "ProgId", @"Applications\Quill.exe");
        registry.SetValue($@"{UserClasses}\Applications\Quill.exe\shell\open\command", null, $"\"{QuillExe}\" \"%1\"");
        registry.SetValue($@"{UserFileExts}\.mkd\UserChoiceLatest\ProgId", "ProgId", "mkd_auto_file");
        registry.SetValue($@"{UserClasses}\mkd_auto_file\shell\open\command", null, $"\"{QuillExe}\" \"%1\"");

        var hook = new MarkdownFileIconHook(registry, ExistingFiles(QuillExe, QuillIcon));
        var result = hook.SyncIfDefault(QuillExe, QuillIcon);

        Assert.Equal([@"Applications\Quill.exe", "mkd_auto_file"], result.MatchedProgIds);
        Assert.Equal([@"Applications\Quill.exe", "mkd_auto_file"], result.UpdatedProgIds);
        Assert.True(result.Notified);
        Assert.Equal(1, registry.Notifications);
    }

    [Fact]
    public void DoesNotClobberAnotherExistingQuillExecutable()
    {
        const string installedExe = @"C:\Tools\Quill\Quill.exe";
        const string devExe = @"D:\Repo\src\Quill\bin\Debug\net10.0-windows10.0.19041.0\Quill.exe";
        const string devIcon = @"D:\Repo\src\Quill\bin\Debug\net10.0-windows10.0.19041.0\Assets\Quill-Markdown.ico";

        var registry = new MemoryRegistry();
        registry.SetValue($@"{UserFileExts}\.md\UserChoiceLatest\ProgId", "ProgId", @"Applications\Quill.exe");
        registry.SetValue($@"{UserClasses}\Applications\Quill.exe\shell\open\command", null, $"\"{installedExe}\" \"%1\"");
        registry.SetValue($@"{UserClasses}\Applications\Quill.exe\DefaultIcon", null, "\"C:\\Tools\\Quill\\Assets\\Quill-Markdown.ico\",0");

        var hook = new MarkdownFileIconHook(registry, ExistingFiles(installedExe, devExe, devIcon));
        var result = hook.SyncIfDefault(devExe, devIcon);

        Assert.Empty(result.MatchedProgIds);
        Assert.Empty(result.UpdatedProgIds);
        Assert.False(result.Notified);
        Assert.Equal("\"C:\\Tools\\Quill\\Assets\\Quill-Markdown.ico\",0", registry.GetValue($@"{UserClasses}\Applications\Quill.exe\DefaultIcon", null));
    }

    [Fact]
    public void RepairsStaleMovedQuillExecutableInHkcu()
    {
        const string oldExe = @"D:\Old\Quill.exe";
        var registry = new MemoryRegistry();
        registry.SetValue($@"{UserFileExts}\.md\UserChoiceLatest\ProgId", "ProgId", @"Applications\Quill.exe");
        registry.SetValue($@"{UserClasses}\Applications\Quill.exe\shell\open\command", null, $"\"{oldExe}\" \"%1\"");

        var hook = new MarkdownFileIconHook(registry, ExistingFiles(QuillExe, QuillIcon));
        var result = hook.SyncIfDefault(QuillExe, QuillIcon);

        Assert.Equal([@"Applications\Quill.exe"], result.MatchedProgIds);
        Assert.Equal([@"Applications\Quill.exe"], result.UpdatedProgIds);
        Assert.True(result.Notified);
        Assert.Equal($"\"{QuillIcon}\",0", registry.GetValue($@"{UserClasses}\Applications\Quill.exe\DefaultIcon", null));
        Assert.Equal($"\"{QuillExe}\",0", registry.GetValue($@"{UserClasses}\Applications\Quill.exe\shell\open", "Icon"));
        Assert.Equal($"\"{QuillExe}\" \"%1\"", registry.GetValue($@"{UserClasses}\Applications\Quill.exe\shell\open\command", null));
    }

    [Fact]
    public void SkipsWhenIconFileIsMissing()
    {
        var registry = new MemoryRegistry();
        registry.SetValue($@"{UserFileExts}\.md\UserChoiceLatest\ProgId", "ProgId", @"Applications\Quill.exe");
        registry.SetValue($@"{UserClasses}\Applications\Quill.exe\shell\open\command", null, $"\"{QuillExe}\" \"%1\"");

        var hook = new MarkdownFileIconHook(registry, ExistingFiles(QuillExe));
        var result = hook.SyncIfDefault(QuillExe, QuillIcon);

        Assert.Empty(result.MatchedProgIds);
        Assert.Empty(result.UpdatedProgIds);
        Assert.False(result.Notified);
        Assert.Equal(0, registry.Notifications);
    }

    [Theory]
    [InlineData(@"C:\Tools\dotnet.exe")]
    [InlineData(@"C:\Repo\Quill.Playground.exe")]
    [InlineData("Quill.exe")]
    [InlineData("")]
    public void SkipsWhenRunningProcessIsNotQuillExe(string runningExe)
    {
        var registry = new MemoryRegistry();
        registry.SetValue($@"{UserFileExts}\.md\UserChoiceLatest\ProgId", "ProgId", @"Applications\Quill.exe");
        registry.SetValue($@"{UserClasses}\Applications\Quill.exe\shell\open\command", null, $"\"{QuillExe}\" \"%1\"");

        var hook = new MarkdownFileIconHook(registry, ExistingFiles(runningExe, QuillExe, QuillIcon));
        var result = hook.SyncIfDefault(runningExe, QuillIcon);

        Assert.Empty(result.MatchedProgIds);
        Assert.Empty(result.UpdatedProgIds);
        Assert.False(result.Notified);
        Assert.Equal(0, registry.Notifications);
    }

    [Fact]
    public void FollowsCurVerProgId()
    {
        var registry = new MemoryRegistry();
        registry.SetValue($@"{UserFileExts}\.md\UserChoiceLatest\ProgId", "ProgId", "Quill.md");
        registry.SetValue($@"{UserClasses}\Quill.md\CurVer", null, "Quill.md.1");
        registry.SetValue($@"{UserClasses}\Quill.md.1\shell\open\command", null, $"\"{QuillExe}\" \"%1\"");

        var hook = new MarkdownFileIconHook(registry, ExistingFiles(QuillExe, QuillIcon));
        var result = hook.SyncIfDefault(QuillExe, QuillIcon);

        Assert.Equal(["Quill.md.1"], result.MatchedProgIds);
        Assert.Equal(["Quill.md.1"], result.UpdatedProgIds);
        Assert.Equal($"\"{QuillIcon}\",0", registry.GetValue($@"{UserClasses}\Quill.md.1\DefaultIcon", null));
        Assert.Null(registry.GetValue($@"{UserClasses}\Quill.md\DefaultIcon", null));
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\Quill, App\\Quill.exe\" \"%1\"", @"C:\Program Files\Quill, App\Quill.exe")]
    [InlineData("C:\\Program Files\\Quill\\Quill.exe \"%1\"", @"C:\Program Files\Quill\Quill.exe")]
    [InlineData(@"C:\Tools\Quill.exe", @"C:\Tools\Quill.exe")]
    [InlineData(@"C:\foo.exe.dir\Quill.exe %1", @"C:\foo.exe.dir\Quill.exe")]
    [InlineData(@"  C:\Tools\Quill.exe --open %1  ", @"C:\Tools\Quill.exe")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("\"unclosed", null)]
    [InlineData("\"\" \"%1\"", null)]
    public void ParsesExecutablePathsFromCommandLines(string command, string? expected)
    {
        Assert.Equal(expected, MarkdownFileIconHook.TryExtractExecutablePath(command));
    }
}
