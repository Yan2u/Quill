namespace Quill.Editor.WinUI.Elements;

/// <summary>
/// How the editor shows the document apart from its theme: behaviour chosen in the settings (14-settings.md),
/// not part of a style sheet (15-themes.md). Immutable; a change lays the realized blocks out again, it
/// does not project again.
/// </summary>
public sealed record MdViewOptions
{
    public static MdViewOptions Default { get; } = new();

    /// <summary>Whether long lines of code blocks wrap; otherwise a block with a longer line scrolls horizontally (Q89).</summary>
    public bool WrapCode { get; init; } = true;

    /// <summary>Whether code blocks show line numbers in a gutter at their left (10).</summary>
    public bool CodeLineNumbers { get; init; }

    /// <summary>Whether code blocks in the <c>math</c> language show their formula, as Mermaid blocks show their diagram (10, Q113).</summary>
    public bool CodeBlockMath { get; init; }
}
