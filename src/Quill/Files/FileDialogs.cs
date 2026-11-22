using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using Quill.Editor.WinUI.Localization;

namespace Quill.Files;

/// <summary>What to do with unsaved changes before they would be lost.</summary>
internal enum UnsavedChoice
{
    Save,
    Discard,
    Cancel,
}

/// <summary>
/// The window's file dialogs: the Windows App SDK pickers (which work without package identity) and
/// the question about unsaved changes, in the Windows 11 style of Notepad〔默认〕.
/// </summary>
internal static class FileDialogs
{
    public static async Task<string?> PickOpenAsync(Window window)
    {
        var picker = new FileOpenPicker(window.AppWindow.Id) { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        foreach (string extension in Quill.Editor.Files.MarkdownFiles.Extensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        picker.FileTypeFilter.Add("*");
        var result = await picker.PickSingleFileAsync();
        return result?.Path;
    }

    /// <summary>Asks for image files (Insert local images, M7.3); empty when cancelled.</summary>
    public static async Task<IReadOnlyList<string>> PickImagesAsync(Window window)
    {
        var picker = new FileOpenPicker(window.AppWindow.Id) { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        foreach (string extension in Quill.Settings.Insertion.ImageFiles.Extensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        var results = await picker.PickMultipleFilesAsync();
        return results is null ? [] : [.. results.Select(static result => result.Path)];
    }

    public static async Task<string?> PickFolderAsync(Window window)
    {
        var picker = new FolderPicker(window.AppWindow.Id) { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        var result = await picker.PickSingleFolderAsync();
        return result?.Path;
    }

    /// <summary>Asks where to save; <paramref name="extension"/> (the setting, with its dot) is suggested first.</summary>
    public static async Task<string?> PickSaveAsync(Window window, string suggestedName, string extension)
    {
        var picker = new FileSavePicker(window.AppWindow.Id)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedName),
            DefaultFileExtension = extension,
        };

        // The first choice is the one the dialog starts with.
        string markdown = UiStrings.Get("dialog.fileType.markdown");
        string text = UiStrings.Get("dialog.fileType.text");
        (string Name, string[] Extensions)[] choices = extension switch
        {
            ".md" => [(markdown, [".md", ".markdown"]), (text, [".txt"])],
            ".markdown" => [(markdown, [".markdown", ".md"]), (text, [".txt"])],
            ".txt" => [(text, [".txt"]), (markdown, [".md", ".markdown"])],
            _ => [(UiStrings.Format("dialog.fileType.custom", extension), [extension]), (markdown, [".md", ".markdown"]), (text, [".txt"])],
        };
        foreach (var (name, extensions) in choices)
        {
            picker.FileTypeChoices.Add(name, extensions);
        }

        var result = await picker.PickSaveFileAsync();
        return result?.Path;
    }

    public static async Task<UnsavedChoice> AskUnsavedAsync(XamlRoot root, string name)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = ThemeOf(root),
            Title = UiStrings.Get("dialog.appTitle"),
            Content = UiStrings.Format("dialog.askUnsaved", name),
            PrimaryButtonText = UiStrings.Get("dialog.save"),
            SecondaryButtonText = UiStrings.Get("dialog.dontSave"),
            CloseButtonText = UiStrings.Get("dialog.cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => UnsavedChoice.Save,
            ContentDialogResult.Secondary => UnsavedChoice.Discard,
            _ => UnsavedChoice.Cancel,
        };
    }

    /// <summary>The file changed on disk while the document has unsaved changes (M6.4): true to reload, false to keep the editor's version.</summary>
    public static async Task<bool> AskReloadAsync(XamlRoot root, string name)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = ThemeOf(root),
            Title = UiStrings.Get("dialog.appTitle"),
            Content = UiStrings.Format("dialog.askReload", name),
            PrimaryButtonText = UiStrings.Get("dialog.reload"),
            CloseButtonText = UiStrings.Get("dialog.keepMine"),
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public static async Task ShowErrorAsync(XamlRoot root, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            RequestedTheme = ThemeOf(root),
            Title = UiStrings.Get("dialog.appTitle"),
            Content = message,
            CloseButtonText = UiStrings.Get("dialog.ok"),
        };
        await dialog.ShowAsync();
    }

    /// <summary>
    /// The theme of the window's content: dialogs open in the popup layer, which does not inherit the theme
    /// the app theme setting puts on the content (14-settings.md).
    /// </summary>
    private static ElementTheme ThemeOf(XamlRoot root) => root.Content is FrameworkElement content ? content.ActualTheme : ElementTheme.Default;
}
