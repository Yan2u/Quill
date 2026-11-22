using Quill.Settings.Themes;

namespace Quill.Settings.Tests;

/// <summary>User theme files (15-themes.md, Q112).</summary>
public sealed class ThemeStoreTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private static ThemeFile Theme(string id = "abc", string name = "Mine") => new()
    {
        Id = id,
        Name = name,
        Description = "Warm colors",
        Created = new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.FromHours(8)),
        Modified = new DateTimeOffset(2026, 11, 3, 11, 30, 0, TimeSpan.FromHours(8)),
        BasedOn = "github",
        Values = new Dictionary<string, string> { ["PageBackground"] = "#FFFDF6E3", ["FontSize"] = "17" },
    };

    [Fact]
    public void ThemesAreWrittenAndReadBack()
    {
        var store = new ThemeStore(_folder.File("themes"));
        Assert.Empty(store.LoadAll());
        store.Save(Theme());
        Assert.Equal([Theme()], store.LoadAll());
        Assert.Equal(["abc.json"], Directory.GetFiles(store.Folder).Select(Path.GetFileName));
    }

    [Fact]
    public void ThemesAreListedByName()
    {
        var store = new ThemeStore(_folder.Path);
        store.Save(Theme("b", "beta"));
        store.Save(Theme("a", "Alpha"));
        Assert.Equal(["Alpha", "beta"], store.LoadAll().Select(static theme => theme.Name));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{ "id": "other", "basedOn": "github" }""")]
    [InlineData("""{ "id": "bad", "name": "No base" }""")]
    [InlineData("[]")]
    public void FilesThatCannotBeReadAreSkipped(string content)
    {
        File.WriteAllText(_folder.File("bad.json"), content);
        var store = new ThemeStore(_folder.Path);
        store.Save(Theme());
        Assert.Equal(["abc"], store.LoadAll().Select(static theme => theme.Id));
    }

    [Fact]
    public void UnknownKeysAndValuesOfOtherTypesAreIgnored()
    {
        File.WriteAllText(_folder.File("x.json"), """
            { "version": 9, "id": "x", "name": "X", "basedOn": "github-dark-dimmed", "shadow": true,
              "values": { "FontSize": "18", "LineHeight": 1.6, "Future": "#FF0000" } }
            """);
        var theme = Assert.Single(new ThemeStore(_folder.Path).LoadAll());
        Assert.Equal("github-dark-dimmed", theme.BasedOn);
        Assert.Equal(new Dictionary<string, string> { ["FontSize"] = "18", ["Future"] = "#FF0000" }, theme.Values);
    }

    [Fact]
    public void DeletingRemovesTheFile()
    {
        var store = new ThemeStore(_folder.Path);
        store.Save(Theme());
        store.Delete("abc");
        store.Delete("../settings");
        Assert.Empty(store.LoadAll());
    }

    [Theory]
    [InlineData("abc", true)]
    [InlineData("a-b_C9", true)]
    [InlineData("", false)]
    [InlineData("../x", false)]
    [InlineData("a b", false)]
    public void IdsAreSafeFileNames(string id, bool valid)
    {
        Assert.Equal(valid, ThemeStore.IsValidId(id));
        Assert.True(ThemeStore.IsValidId(ThemeStore.NewId()));
    }

    [Fact]
    public void CopiesAreNamedAfterTheOriginal()
    {
        Assert.Equal("Github copy", ThemeStore.CopyName("Github", ["Github"]));
        Assert.Equal("Github copy 2", ThemeStore.CopyName("Github", ["Github", "github COPY"]));
        Assert.Equal("Github copy 3", ThemeStore.CopyName("Github", ["Github copy", "Github copy 2"]));
    }
}
