namespace Quill.Settings;

/// <summary>What the first window opens when no file is given on the command line (14-settings.md, Q113).</summary>
public enum StartupAction
{
    NewFile,
    LastFolder,
    LastFolderAndFile,

    /// <summary>The folder in <see cref="AppSettings.StartupFolder"/>.</summary>
    Folder,
}

/// <summary>The extension the save dialog suggests.</summary>
public enum FileExtensionChoice
{
    Md,
    Markdown,
    Txt,

    /// <summary>The extension in <see cref="AppSettings.CustomExtension"/>.</summary>
    Custom,
}

/// <summary>The line break of new documents (Q103).</summary>
public enum LineBreakChoice
{
    Lf,
    CrLf,
}

/// <summary>What dropping a folder on the window does.</summary>
public enum FolderDropAction
{
    /// <summary>Shows it as the root of the sidebar's Files tab.</summary>
    Open,
    InsertLink,
}

/// <summary>What dropping a Markdown file on the window does.</summary>
public enum MarkdownDropAction
{
    Open,
    InsertLink,
}

/// <summary>What dropping a file that is neither Markdown nor an image does (Q108).</summary>
public enum OtherFileDropAction
{
    InsertLink,
    None,
}

/// <summary>Where an inserted image is copied (Q109).</summary>
public enum ImageInsertRule
{
    /// <summary>Nothing is copied: a local image is inserted as a file URL, an online one as its URL.</summary>
    None,
    CopyToCurrentFolder,

    /// <summary><c>./assets</c>.</summary>
    CopyToAssets,

    /// <summary><c>./${fileNameNoExt}.assets</c>.</summary>
    CopyToFileAssets,

    /// <summary>The path in <see cref="AppSettings.ImageFolder"/>, which may use macros.</summary>
    CopyToFolder,
}

/// <summary>The appearance of the application's frame, which also picks the editor's theme.</summary>
public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>The language of the interface; a placeholder until localization (Q113).</summary>
public enum DisplayLanguage
{
    English,
    Chinese,
}
