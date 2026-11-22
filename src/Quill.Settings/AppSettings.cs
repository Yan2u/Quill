using System.Text.RegularExpressions;

namespace Quill.Settings;

/// <summary>
/// The user's settings (14-settings.md), immutable. Every window of the process shares one
/// <see cref="SettingsStore"/>; a change is a new instance. Defaults are those of the specification.
/// </summary>
public sealed partial record AppSettings
{
    public const int DefaultReadingSpeed = 382;
    public const int MinReadingSpeed = 1;
    public const int MaxReadingSpeed = 10000;
    public const string DefaultLightTheme = "github";
    public const string DefaultDarkTheme = "github-dark-dimmed";
    public const string AutoSpellCheckLanguage = "auto";
    public const string NoSpellCheckLanguage = "off";

    /// <summary>The reading speed presets: slow, medium, fast (Q105).</summary>
    public static IReadOnlyList<int> ReadingSpeedPresets { get; } = [120, 240, 382];

    public static AppSettings Default { get; } = new();

    // File

    public StartupAction Startup { get; init; } = StartupAction.NewFile;

    /// <summary>The folder of <see cref="StartupAction.Folder"/>; null until one was chosen.</summary>
    public string? StartupFolder { get; init; }

    public FileExtensionChoice DefaultExtension { get; init; } = FileExtensionChoice.Md;

    /// <summary>The extension of <see cref="FileExtensionChoice.Custom"/>, with its dot (<see cref="IsValidExtension"/>).</summary>
    public string? CustomExtension { get; init; }

    /// <summary>Saves a document with a file about a second after editing stops, and when the window loses focus or closes (Q95).</summary>
    public bool AutoSave { get; init; }

    /// <summary>Saves the document before another file replaces it in the window; see <see cref="SavesOnSwitch"/> (Q104).</summary>
    public bool AutoSaveOnSwitch { get; init; }

    public FolderDropAction DropFolder { get; init; } = FolderDropAction.Open;

    public MarkdownDropAction DropMarkdown { get; init; } = MarkdownDropAction.Open;

    public OtherFileDropAction DropOtherFile { get; init; } = OtherFileDropAction.None;

    // Editing

    /// <summary>Pairs brackets and quotes (Q107).</summary>
    public bool MatchBrackets { get; init; } = true;

    /// <summary>The plain text Ctrl+C puts on the clipboard is the selection's Markdown source (Q102).</summary>
    public bool CopyMarkdownSource { get; init; } = true;

    /// <summary>Copy and cut without a selection take the line at the caret (Q110).</summary>
    public bool CopyLineWithoutSelection { get; init; }

    /// <summary>The line break of new documents; opened files keep theirs (Q103).</summary>
    public LineBreakChoice DefaultLineBreak { get; init; } = LineBreakChoice.CrLf;

    /// <summary><see cref="AutoSpellCheckLanguage"/>, <see cref="NoSpellCheckLanguage"/> or a language tag; a placeholder (Q113).</summary>
    public string SpellCheckLanguage { get; init; } = AutoSpellCheckLanguage;

    // Image

    public ImageInsertRule ImageRule { get; init; } = ImageInsertRule.None;

    /// <summary>The folder of <see cref="ImageInsertRule.CopyToFolder"/>; may use macros (Q109).</summary>
    public string? ImageFolder { get; init; }

    public bool ApplyImageRuleToLocal { get; init; } = true;

    public bool ApplyImageRuleToOnline { get; init; }

    /// <summary>Percent-encodes spaces and non-ASCII characters of inserted image URLs.</summary>
    public bool EscapeImageUrls { get; init; }

    // Markdown

    public bool CodeLineNumbers { get; init; }

    /// <summary>Long lines of code blocks wrap; otherwise an overflowing block scrolls (Q89).</summary>
    public bool WrapCode { get; init; } = true;

    /// <summary>Code blocks in the <c>math</c> language show as formulas (Q113).</summary>
    public bool CodeBlockMath { get; init; }

    // Appearance

    public bool ShowStatusBar { get; init; } = true;

    /// <summary>Words per minute for the reading time (Q105).</summary>
    public int ReadingSpeed { get; init; } = DefaultReadingSpeed;

    public AppTheme AppTheme { get; init; } = AppTheme.System;

    /// <summary>The id of the editor theme while the app is light (15-themes.md).</summary>
    public string LightTheme { get; init; } = DefaultLightTheme;

    /// <summary>The id of the editor theme while the app is dark.</summary>
    public string DarkTheme { get; init; } = DefaultDarkTheme;

    // General

    public DisplayLanguage Language { get; init; } = DisplayLanguage.English;

    /// <summary>
    /// Whether to save before another file replaces the document: always while auto-save is on (the
    /// switch is then on and read-only), otherwise as chosen (Q104).
    /// </summary>
    public bool SavesOnSwitch => AutoSave || AutoSaveOnSwitch;

    /// <summary>The extension the save dialog suggests, with its dot.</summary>
    public string FileExtension => DefaultExtension switch
    {
        FileExtensionChoice.Markdown => ".markdown",
        FileExtensionChoice.Txt => ".txt",
        FileExtensionChoice.Custom when CustomExtension is { } custom && IsValidExtension(custom) => custom,
        _ => ".md",
    };

    /// <summary>A custom extension: a dot, then letters, digits, '_' and '-'.</summary>
    public static bool IsValidExtension(string? extension) => extension is not null && ExtensionPattern().IsMatch(extension);

    /// <summary>The settings with every value in its range: invalid ones take their default.</summary>
    public AppSettings Normalize()
    {
        var result = this;
        if (ReadingSpeed is < MinReadingSpeed or > MaxReadingSpeed)
        {
            result = result with { ReadingSpeed = DefaultReadingSpeed };
        }

        if (CustomExtension is not null && !IsValidExtension(CustomExtension))
        {
            result = result with { CustomExtension = null };
        }

        if (DefaultExtension == FileExtensionChoice.Custom && result.CustomExtension is null)
        {
            result = result with { DefaultExtension = FileExtensionChoice.Md };
        }

        if (Startup == StartupAction.Folder && string.IsNullOrWhiteSpace(StartupFolder))
        {
            result = result with { Startup = StartupAction.NewFile, StartupFolder = null };
        }

        if (string.IsNullOrWhiteSpace(LightTheme))
        {
            result = result with { LightTheme = DefaultLightTheme };
        }

        if (string.IsNullOrWhiteSpace(DarkTheme))
        {
            result = result with { DarkTheme = DefaultDarkTheme };
        }

        if (string.IsNullOrWhiteSpace(SpellCheckLanguage))
        {
            result = result with { SpellCheckLanguage = AutoSpellCheckLanguage };
        }

        return result;
    }

    [GeneratedRegex(@"^\.[A-Za-z0-9_-]+$")]
    private static partial Regex ExtensionPattern();
}
