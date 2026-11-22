using System.Text.Json.Nodes;

namespace Quill.Settings.Tests;

/// <summary>The JSON storage of settings and application state (14-settings.md).</summary>
public sealed class SettingsStoreTests
{
    [Fact]
    public void AMissingFileGivesTheDefaults()
    {
        using var folder = new TempFolder();
        var store = new SettingsStore(folder.File("settings.json"));
        Assert.Equal(AppSettings.Default, store.Current);
        Assert.False(File.Exists(store.FilePath));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("[1, 2]")]
    [InlineData("42")]
    public void ABrokenFileGivesTheDefaults(string content)
    {
        using var folder = new TempFolder();
        File.WriteAllText(folder.File("settings.json"), content);
        Assert.Equal(AppSettings.Default, new SettingsStore(folder.File("settings.json")).Current);
    }

    [Fact]
    public void ChangesAreWrittenAndReadBack()
    {
        using var folder = new TempFolder();
        string path = folder.File("settings.json");
        var changed = new AppSettings
        {
            Startup = StartupAction.Folder,
            StartupFolder = @"C:\notes",
            DefaultExtension = FileExtensionChoice.Custom,
            CustomExtension = ".mdx",
            AutoSave = true,
            DropOtherFile = OtherFileDropAction.InsertLink,
            CopyMarkdownSource = false,
            DefaultLineBreak = LineBreakChoice.Lf,
            SpellCheckLanguage = "en-US",
            ImageRule = ImageInsertRule.CopyToFolder,
            ImageFolder = "${dirPath}/images",
            WrapCode = false,
            ReadingSpeed = 240,
            AppTheme = AppTheme.Dark,
            LightTheme = "my-theme",
            Language = DisplayLanguage.Chinese,
        };
        var store = new SettingsStore(path);
        Assert.True(store.Update(_ => changed));
        Assert.Equal(changed, store.Current);
        Assert.Equal(changed, new SettingsStore(path).Current);
    }

    [Fact]
    public void EnumsAreWrittenAsCamelCaseNamesWithAVersion()
    {
        using var folder = new TempFolder();
        var store = new SettingsStore(folder.File("settings.json"));
        store.Update(settings => settings with { Startup = StartupAction.LastFolderAndFile });
        var json = JsonNode.Parse(File.ReadAllText(store.FilePath))!.AsObject();
        Assert.Equal("lastFolderAndFile", json["startup"]!.GetValue<string>());
        Assert.Equal("crLf", json["defaultLineBreak"]!.GetValue<string>());
        Assert.Equal(SettingsStore.CurrentVersion, json["version"]!.GetValue<int>());
    }

    [Fact]
    public void ValuesThatCannotBeReadTakeTheirDefaults()
    {
        using var folder = new TempFolder();
        string path = folder.File("settings.json");
        File.WriteAllText(path, """
            {
              "version": 1,
              "autoSave": "yes",
              "readingSpeed": -5,
              "appTheme": "purple",
              "startup": 3,
              "language": "1",
              "wrapCode": false,
              "lightTheme": 12,
            }
            """);
        var settings = new SettingsStore(path).Current;
        Assert.False(settings.AutoSave);
        Assert.Equal(AppSettings.DefaultReadingSpeed, settings.ReadingSpeed);
        Assert.Equal(AppTheme.System, settings.AppTheme);
        Assert.Equal(StartupAction.NewFile, settings.Startup);
        Assert.Equal(DisplayLanguage.English, settings.Language);
        Assert.Equal(AppSettings.DefaultLightTheme, settings.LightTheme);
        Assert.False(settings.WrapCode);
    }

    [Fact]
    public void UnknownKeysAreKeptWhenWritingAgain()
    {
        using var folder = new TempFolder();
        string path = folder.File("settings.json");
        File.WriteAllText(path, """{ "version": 1, "futureSetting": { "a": [1, 2] }, "showStatusBar": true }""");
        var store = new SettingsStore(path);
        store.Update(settings => settings with { ShowStatusBar = false });
        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.Equal(2, json["futureSetting"]!["a"]!.AsArray().Count);
        Assert.False(json["showStatusBar"]!.GetValue<bool>());
    }

    [Fact]
    public void AFileOfANewerVersionKeepsItsVersion()
    {
        using var folder = new TempFolder();
        string path = folder.File("settings.json");
        File.WriteAllText(path, """{ "version": 7 }""");
        new SettingsStore(path).Update(settings => settings with { AutoSave = true });
        Assert.Equal(7, JsonNode.Parse(File.ReadAllText(path))!["version"]!.GetValue<int>());
    }

    [Fact]
    public void AnUpdateWithoutChangeNeitherWritesNorRaises()
    {
        using var folder = new TempFolder();
        var store = new SettingsStore(folder.File("settings.json"));
        int raised = 0;
        store.Changed += (_, _) => raised++;
        Assert.False(store.Update(settings => settings with { }));
        Assert.Equal(0, raised);
        Assert.False(File.Exists(store.FilePath));
    }

    [Fact]
    public void ChangedReportsThePreviousAndTheCurrentValue()
    {
        using var folder = new TempFolder();
        var store = new SettingsStore(folder.File("settings.json"));
        StoreChangedEventArgs<AppSettings>? args = null;
        store.Changed += (_, e) => args = e;
        store.Update(settings => settings with { ReadingSpeed = 120 });
        Assert.NotNull(args);
        Assert.Equal(382, args.Previous.ReadingSpeed);
        Assert.Equal(120, args.Current.ReadingSpeed);
    }

    [Fact]
    public void UpdatesAreNormalized()
    {
        using var folder = new TempFolder();
        var store = new SettingsStore(folder.File("settings.json"));
        store.Update(settings => settings with { ReadingSpeed = 0, DefaultExtension = FileExtensionChoice.Custom, CustomExtension = "nodot" });
        Assert.Equal(AppSettings.DefaultReadingSpeed, store.Current.ReadingSpeed);
        Assert.Equal(FileExtensionChoice.Md, store.Current.DefaultExtension);
    }

    [Fact]
    public void WritingLeavesNoTemporaryFile()
    {
        using var folder = new TempFolder();
        var store = new SettingsStore(folder.File("settings.json"));
        store.Update(settings => settings with { AutoSave = true });
        store.Update(settings => settings with { AutoSave = false });
        Assert.Equal(["settings.json"], Directory.GetFiles(folder.Path).Select(Path.GetFileName));
        Assert.Null(store.LastWriteError);
    }

    [Fact]
    public void TheFolderIsCreatedWhenFirstWritten()
    {
        using var folder = new TempFolder();
        var store = new SettingsStore(Path.Combine(folder.Path, "Quill", "settings.json"));
        store.Update(settings => settings with { AutoSave = true });
        Assert.True(new SettingsStore(store.FilePath).Current.AutoSave);
    }

    [Fact]
    public void AFileThatCannotBeWrittenKeepsTheValueInMemory()
    {
        using var folder = new TempFolder();
        string path = folder.File("settings.json");

        // A folder in the place of the file: the write fails.
        Directory.CreateDirectory(path);
        var store = new SettingsStore(path);
        Assert.True(store.Update(settings => settings with { AutoSave = true }));
        Assert.True(store.Current.AutoSave);
        Assert.NotNull(store.LastWriteError);
    }

    [Fact]
    public void TheDefaultFilesAreInTheApplicationsDataFolder()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quill");
        Assert.Equal(Path.Combine(root, "settings.json"), SettingsStore.DefaultPath);
        Assert.Equal(Path.Combine(root, "state.json"), AppStateStore.DefaultPath);
    }

    [Fact]
    public void TheApplicationStateIsWrittenAndReadBack()
    {
        using var folder = new TempFolder();
        string path = folder.File("state.json");
        var state = new AppState
        {
            SidebarVisible = true,
            SidebarWidth = 333.5,
            SidebarTab = SidebarTabState.Files,
            FilesView = FilesViewState.List,
            NavigationExpanded = true,
            LastFolder = @"C:\notes",
            LastFile = @"C:\notes\a.md",
        };
        new AppStateStore(path).Update(_ => state);
        Assert.Equal(state, new AppStateStore(path).Current);
    }

    [Theory]
    [InlineData(10, AppState.MinSidebarWidth)]
    [InlineData(5000, AppState.MaxSidebarWidth)]
    [InlineData(300, 300)]
    public void TheSidebarWidthStaysInItsRange(double width, double expected)
    {
        using var folder = new TempFolder();
        string path = folder.File("state.json");
        File.WriteAllText(path, $$"""{ "sidebarWidth": {{width}} }""");
        Assert.Equal(expected, new AppStateStore(path).Current.SidebarWidth);
    }

    [Fact]
    public void TheWindowSizeAndMaximizedStateAreWrittenAndReadBack()
    {
        using var folder = new TempFolder();
        string path = folder.File("state.json");
        var state = new AppState
        {
            WindowWidth = 1280,
            WindowHeight = 820,
            WindowMaximized = true,
        };
        new AppStateStore(path).Update(_ => state);
        var loaded = new AppStateStore(path).Current;
        Assert.Equal(1280, loaded.WindowWidth);
        Assert.Equal(820, loaded.WindowHeight);
        Assert.True(loaded.WindowMaximized);
    }

    [Theory]
    [InlineData(100, 120, AppState.MinWindowWidth, AppState.MinWindowHeight)]
    [InlineData(32000, 20000, AppState.MaxWindowWidth, AppState.MaxWindowHeight)]
    [InlineData(1200, null, null, null)]
    [InlineData(0, 800, null, null)]
    [InlineData(-400, 800, null, null)]
    public void WindowSizesAreNormalizedAndInvalidOnesCleared(int? width, int? height, int? expectedWidth, int? expectedHeight)
    {
        var normalized = (new AppState { WindowWidth = width, WindowHeight = height }).Normalize();
        Assert.Equal(expectedWidth, normalized.WindowWidth);
        Assert.Equal(expectedHeight, normalized.WindowHeight);
    }

    [Fact]
    public void InvalidWindowSizeJsonFallsBackToNull()
    {
        using var folder = new TempFolder();
        string path = folder.File("state.json");
        File.WriteAllText(path, """{ "windowWidth": "wide", "windowHeight": 720, "windowMaximized": "yes" }""");
        var loaded = new AppStateStore(path).Current;
        Assert.Null(loaded.WindowWidth);
        Assert.Null(loaded.WindowHeight);
        Assert.False(loaded.WindowMaximized);
    }
}
