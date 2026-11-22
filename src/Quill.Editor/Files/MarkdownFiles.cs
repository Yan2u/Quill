using System.Globalization;

namespace Quill.Editor.Files;

/// <summary>What a folder holds for the sidebar: its visible subfolders and Markdown files, each sorted by name.</summary>
public sealed record FolderContent(IReadOnlyList<string> Folders, IReadOnlyList<string> Files);

/// <summary>
/// What counts as a Markdown file (Q106): the file tree, the file list, the open dialog and drag and
/// drop share this definition.
/// </summary>
public static class MarkdownFiles
{
    private static readonly EnumerationOptions Visible = new()
    {
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        IgnoreInaccessible = true,
    };

    public static IReadOnlyList<string> Extensions { get; } = [".md", ".markdown", ".mdown", ".mkd", ".txt"];

    public static bool IsMarkdown(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reads a folder: subfolders and Markdown files, in natural order (file2 before file10). Hidden entries
    /// and those starting with a dot (.git) are left out〔默认〕; a folder that cannot be read is empty.
    /// </summary>
    public static FolderContent Read(string folder)
    {
        try
        {
            var byName = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);
            var entries = new DirectoryInfo(folder).EnumerateFileSystemInfos("*", Visible).Where(static entry => !entry.Name.StartsWith('.')).ToList();
            return new FolderContent(
                [.. entries.OfType<DirectoryInfo>().OrderBy(static entry => entry.Name, byName).Select(static entry => entry.FullName)],
                [.. entries.OfType<FileInfo>().Where(static entry => IsMarkdown(entry.Name)).OrderBy(static entry => entry.Name, byName).Select(static entry => entry.FullName)]);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return new FolderContent([], []);
        }
    }
}
