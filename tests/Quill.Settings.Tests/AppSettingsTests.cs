namespace Quill.Settings.Tests;

/// <summary>The defaults and rules of the settings model (14-settings.md).</summary>
public sealed class AppSettingsTests
{
    [Fact]
    public void DefaultsAreThoseOfTheSpecification()
    {
        var d = AppSettings.Default;
        Assert.Equal(StartupAction.NewFile, d.Startup);
        Assert.Equal(FileExtensionChoice.Md, d.DefaultExtension);
        Assert.False(d.AutoSave);
        Assert.False(d.AutoSaveOnSwitch);
        Assert.Equal(FolderDropAction.Open, d.DropFolder);
        Assert.Equal(MarkdownDropAction.Open, d.DropMarkdown);
        Assert.Equal(OtherFileDropAction.None, d.DropOtherFile);
        Assert.True(d.MatchBrackets);
        Assert.True(d.CopyMarkdownSource);
        Assert.False(d.CopyLineWithoutSelection);
        Assert.Equal(LineBreakChoice.CrLf, d.DefaultLineBreak);
        Assert.Equal(AppSettings.AutoSpellCheckLanguage, d.SpellCheckLanguage);
        Assert.Equal(ImageInsertRule.None, d.ImageRule);
        Assert.True(d.ApplyImageRuleToLocal);
        Assert.False(d.ApplyImageRuleToOnline);
        Assert.False(d.EscapeImageUrls);
        Assert.False(d.CodeLineNumbers);
        Assert.True(d.WrapCode);
        Assert.False(d.CodeBlockMath);
        Assert.True(d.ShowStatusBar);
        Assert.Equal(382, d.ReadingSpeed);
        Assert.Equal(AppTheme.System, d.AppTheme);
        Assert.Equal("github", d.LightTheme);
        Assert.Equal("github-dark-dimmed", d.DarkTheme);
        Assert.Equal(DisplayLanguage.English, d.Language);
        Assert.Equal([120, 240, 382], AppSettings.ReadingSpeedPresets);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void AutoSaveTurnsSavingOnSwitchOn(bool autoSave, bool onSwitch, bool saves)
    {
        Assert.Equal(saves, new AppSettings { AutoSave = autoSave, AutoSaveOnSwitch = onSwitch }.SavesOnSwitch);
    }

    [Theory]
    [InlineData(".md", true)]
    [InlineData(".my-notes_2", true)]
    [InlineData("md", false)]
    [InlineData(".", false)]
    [InlineData(".a.b", false)]
    [InlineData(".a b", false)]
    [InlineData(".\u00E9", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void CustomExtensionsStartWithADotThenWordCharacters(string? extension, bool valid)
    {
        Assert.Equal(valid, AppSettings.IsValidExtension(extension));
    }

    [Theory]
    [InlineData(FileExtensionChoice.Md, null, ".md")]
    [InlineData(FileExtensionChoice.Markdown, null, ".markdown")]
    [InlineData(FileExtensionChoice.Txt, null, ".txt")]
    [InlineData(FileExtensionChoice.Custom, ".mdx", ".mdx")]
    public void TheSaveDialogSuggestsTheChosenExtension(FileExtensionChoice choice, string? custom, string expected)
    {
        Assert.Equal(expected, new AppSettings { DefaultExtension = choice, CustomExtension = custom }.FileExtension);
    }

    [Fact]
    public void NormalizeReplacesValuesOutOfRange()
    {
        var settings = new AppSettings
        {
            ReadingSpeed = 0,
            DefaultExtension = FileExtensionChoice.Custom,
            CustomExtension = "bad",
            Startup = StartupAction.Folder,
            StartupFolder = " ",
            LightTheme = "",
            SpellCheckLanguage = "",
        }.Normalize();
        Assert.Equal(AppSettings.DefaultReadingSpeed, settings.ReadingSpeed);
        Assert.Equal(FileExtensionChoice.Md, settings.DefaultExtension);
        Assert.Null(settings.CustomExtension);
        Assert.Equal(StartupAction.NewFile, settings.Startup);
        Assert.Equal(AppSettings.DefaultLightTheme, settings.LightTheme);
        Assert.Equal(AppSettings.AutoSpellCheckLanguage, settings.SpellCheckLanguage);
    }

    [Fact]
    public void NormalizeKeepsValidSettings()
    {
        var settings = new AppSettings { ReadingSpeed = 120, Startup = StartupAction.Folder, StartupFolder = @"C:\notes" };
        Assert.Same(settings, settings.Normalize());
        Assert.Same(AppSettings.Default, AppSettings.Default.Normalize());
    }
}
