namespace Quill.Settings.Explorer;

/// <summary>Whether File Explorer's New menu offers a Markdown document.</summary>
public enum NewMenuState
{
    NotAdded,

    /// <summary>Quill added it, so Quill can remove it.</summary>
    AddedByQuill,

    /// <summary>Another program (or the user) added it: Quill leaves it alone.</summary>
    AddedElsewhere,
}
