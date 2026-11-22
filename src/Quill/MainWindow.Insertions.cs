using Quill.Editor.Files;
using Quill.Files;
using Quill.Shell.Insertion;

namespace Quill;

/// <summary>
/// Files and images in the editor (M7.3): drops and pastes are handled by <see cref="EditorInsertions"/> with
/// this window's settings and document; a dropped folder shows in the sidebar, dropped Markdown files open
/// (the first in this window, after asking about unsaved changes; the others in new windows〔默认〕).
/// </summary>
public sealed partial class MainWindow
{
    private void InitializeInsertions()
    {
        var insertions = new EditorInsertions(() => Settings, () => _path)
        {
            OpenFolder = ShowFolder,
            OpenFiles = OpenDroppedFilesAsync,
            PickImages = () => FileDialogs.PickImagesAsync(this),
        };
        insertions.Failed += async (_, message) => await FileDialogs.ShowErrorAsync(RootGrid.XamlRoot, message);
        _editor.Insertions = insertions;
    }

    private async Task OpenDroppedFilesAsync(IReadOnlyList<string> paths)
    {
        var others = paths.Skip(1).ToList();
        string first = paths[0];
        if (!string.Equals(first, _path, StringComparison.OrdinalIgnoreCase) && await ConfirmSwitchAsync())
        {
            await OpenFileAsync(first);
        }

        foreach (string path in others.Where(MarkdownFiles.IsMarkdown))
        {
            App.OpenWindow(path);
        }
    }
}
