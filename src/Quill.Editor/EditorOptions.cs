namespace Quill.Editor;

/// <summary>
/// User settings that affect editing and export, passed as immutable data (01-architecture.md). The core
/// never reads the settings; the app maps them to these options (14-settings.md). The defaults are the
/// settings' defaults.
/// </summary>
public sealed record EditorOptions
{
    public static EditorOptions Default { get; } = new();

    /// <summary>
    /// When two lists of the same type are adjacent, write the second with the other marker character
    /// ('-'/'*', '.'/')') so that reopening keeps them apart (Q25). On by default.
    /// </summary>
    public bool SwitchMarkerForAdjacentLists { get; init; } = true;

    /// <summary>Pair brackets and quotes as they are typed (04, Q107, Q115). On by default.</summary>
    public bool MatchBrackets { get; init; } = true;

    /// <summary>The plain text of Ctrl+C is the selection's Markdown source; off, the text as shown (Q102). On by default.</summary>
    public bool CopyMarkdownSource { get; init; } = true;

    /// <summary>Copy and cut without a selection take the block (or code line) at the caret (Q110, Q116). Off by default.</summary>
    public bool CopyLineWithoutSelection { get; init; }
}
