namespace Quill.Settings.Insertion;

/// <summary>
/// The macros of the image folder (14-settings.md, Q109): <c>${fileName}</c> (the document's file name with its
/// extension), <c>${fileNameNoExt}</c>, <c>${dirName}</c> (the name of the document's folder) and
/// <c>${dirPath}</c> (its full path). Names are matched without case; an unknown macro stays as written.
/// </summary>
public static class ImageFolderMacros
{
    public const string FileName = "${fileName}";
    public const string FileNameNoExt = "${fileNameNoExt}";
    public const string DirName = "${dirName}";
    public const string DirPath = "${dirPath}";

    /// <summary>Whether the template uses a macro, which needs a document with a file.</summary>
    public static bool UsesMacros(string template) =>
        new[] { FileName, FileNameNoExt, DirName, DirPath }.Any(macro => template.Contains(macro, StringComparison.OrdinalIgnoreCase));

    /// <summary>Expands the macros for the document at <paramref name="documentPath"/>.</summary>
    public static string Expand(string template, string documentPath)
    {
        string full = Path.GetFullPath(documentPath);
        string folder = Path.GetDirectoryName(full) ?? string.Empty;
        string folderName = Path.GetFileName(folder.TrimEnd('\\', '/'));
        return template
            .Replace(FileNameNoExt, Path.GetFileNameWithoutExtension(full), StringComparison.OrdinalIgnoreCase)
            .Replace(FileName, Path.GetFileName(full), StringComparison.OrdinalIgnoreCase)
            .Replace(DirName, folderName.Length > 0 ? folderName : folder, StringComparison.OrdinalIgnoreCase)
            .Replace(DirPath, folder, StringComparison.OrdinalIgnoreCase);
    }
}
