namespace Quill.Highlighting;

/// <summary>The themes of the source mode (Q85): VS Code's Light+ and Dark+, following the app theme.</summary>
public enum HighlightTheme
{
    Light,
    Dark,
}

[Flags]
public enum HighlightFontStyle
{
    None = 0,
    Italic = 1,
    Bold = 2,
    Underline = 4,
    Strikethrough = 8,
}

/// <summary>
/// A colored run of one line: <see cref="Start"/> and <see cref="Length"/> in UTF-16 code units of the
/// line, the foreground as 0xAARRGGBB and the font style. Runs cover the line without gaps.
/// </summary>
public readonly record struct HighlightSpan(int Start, int Length, uint Foreground, HighlightFontStyle Style);
