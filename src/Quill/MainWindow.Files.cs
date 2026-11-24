using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quill.Editor.Documents;
using Quill.Editor.Files;
using Quill.Editor.Markdown;
using Quill.Editor.WinUI.Localization;
using Quill.Files;

namespace Quill;

/// <summary>
/// The file features of M6.4: recent files (File → Open Recent), reopening with a chosen encoding,
/// following changes other programs make to the open file, and auto-save.
/// </summary>
public sealed partial class MainWindow
{
    private AutoSaver? _autoSave;
    private FileChangeMonitor? _monitor;
    private bool _warnedEncoding;

    private void InitializeFiles()
    {
        App.RecentFiles.Changed += OnRecentFilesChanged;
        BuildRecentMenu();
        UpdateSystemEncodingLabel();

        _autoSave = new AutoSaver(
            canSave: () => _path is not null && _editor.IsModified && !_asking,
            save: () => SaveAsync(saveAs: false),
            dispatch: action => DispatcherQueue.TryEnqueue(() => action()))
        {
            IsEnabled = Settings.AutoSave,
        };
        AutoSaveItem.IsChecked = _autoSave.IsEnabled;
        _editor.Changed += (_, _) => _autoSave.NotifyEdited();
        Activated += async (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                await _autoSave.NotifyLeavingAsync();
            }
        };
        Closed += (_, _) =>
        {
            App.RecentFiles.Changed -= OnRecentFilesChanged;
            _monitor?.Dispose();
            _autoSave.Dispose();
        };
    }

    private void OnRecentFilesChanged(object? sender, EventArgs e)
    {
        if (_closed)
        {
            return;
        }

        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => OnRecentFilesChanged(sender, e));
            return;
        }

        BuildRecentMenu();
        if (PageHost.Children.FirstOrDefault() is Shell.Pages.OpenPage open)
        {
            open.SetRecent(App.RecentFiles.Paths);
        }
    }

    /// <summary>Follows the file of the shown document; none for an untitled one.</summary>
    private void WatchFile(string? path)
    {
        if (_monitor?.Path is { } watched && path is not null && string.Equals(watched, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
        {
            _monitor.Acknowledge();
            return;
        }

        _monitor?.Dispose();
        _monitor = null;
        _warnedEncoding = false;
        if (path is null || Path.GetDirectoryName(Path.GetFullPath(path)) is not { } folder || !Directory.Exists(folder))
        {
            return;
        }

        var monitor = new FileChangeMonitor(path);
        monitor.Changed += (_, kind) => DispatcherQueue.TryEnqueue(async () => await OnFileChangedAsync(monitor, kind));
        _monitor = monitor;
    }

    /// <summary>
    /// Another program changed the open file (Typora): without unsaved changes it is reloaded silently;
    /// with them the user chooses between reloading and keeping the editor's version. A reload is one undo
    /// item. A deleted file marks the document modified, so saving writes it again.
    /// </summary>
    private async Task OnFileChangedAsync(FileChangeMonitor monitor, FileChangeKind kind)
    {
        if (!ReferenceEquals(monitor, _monitor) || _path is null)
        {
            return;
        }

        if (kind == FileChangeKind.Deleted)
        {
            _editor.MarkUnsaved();
            UpdateTitle();
            return;
        }

        if (_editor.IsModified)
        {
            if (_asking)
            {
                // Another dialog is open: the next change event asks again.
                return;
            }

            _asking = true;
            bool reload;
            try
            {
                reload = await FileDialogs.AskReloadAsync(RootGrid.XamlRoot, DisplayName);
            }
            finally
            {
                _asking = false;
            }

            if (!reload)
            {
                monitor.Acknowledge();
                _editor.Focus(FocusState.Programmatic);
                return;
            }
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(_path);
            monitor.Acknowledge();
            _editor.Reload(bytes, ReopenEncoding());
            UpdateTitle();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Still locked or gone again: a later event reports it.
        }
    }

    /// <summary>The encoding to read the file with again: the one it was opened with, when the user chose it.</summary>
    private TextEncodingChoice? ReopenEncoding() => _chosenEncoding;

    private TextEncodingChoice? _chosenEncoding;

    private void BuildRecentMenu()
    {
        RecentMenu.Items.Clear();
        var paths = App.RecentFiles.Paths;
        foreach (string path in paths)
        {
            var item = new MenuFlyoutItem { Text = path, Tag = path };
            item.Click += async (_, _) => await OpenRecentAsync(path);
            RecentMenu.Items.Add(item);
        }

        if (paths.Count > 0)
        {
            RecentMenu.Items.Add(new MenuFlyoutSeparator());
        }

        var clear = new MenuFlyoutItem { Text = UiStrings.Get("file.clearRecent"), IsEnabled = paths.Count > 0 };
        clear.Click += (_, _) => App.RecentFiles.Clear();
        RecentMenu.Items.Add(clear);
    }

    private void UpdateSystemEncodingLabel()
    {
        string legacy = MarkdownFileCodec.NameOf(DocumentFormat.Default with { Encoding = TextEncodingKind.Legacy, CodePage = MarkdownFileCodec.SystemCodePage });
        SystemEncodingItem.Text = $"{UiStrings.Get("file.encoding.systemDefault")} ({legacy})";
    }

    private async Task OpenRecentAsync(string path)
    {
        if (!File.Exists(path))
        {
            App.RecentFiles.Remove(path);
            await FileDialogs.ShowErrorAsync(RootGrid.XamlRoot, UiStrings.Format("dialog.recentMissing", path));
            return;
        }

        if (await ConfirmSwitchAsync())
        {
            await OpenFileAsync(path);
        }
    }

    private async void OnReopenWithEncodingClick(object sender, RoutedEventArgs e)
    {
        if (_path is not { } path || sender is not MenuFlyoutItem { Tag: string tag })
        {
            return;
        }

        var choice = tag switch
        {
            "utf-16le" => TextEncodingChoice.Utf16LittleEndian,
            "utf-16be" => TextEncodingChoice.Utf16BigEndian,
            "gb18030" => TextEncodingChoice.Gb18030,
            "system" => new TextEncodingChoice(TextEncodingKind.Legacy),
            _ => TextEncodingChoice.Utf8,
        };
        if (await ConfirmDiscardAsync())
        {
            await OpenFileAsync(path, choice);
        }
    }

    /// <summary>File > Auto Save is the setting (14-settings.md): every window follows it.</summary>
    private void OnAutoSaveClick(object sender, RoutedEventArgs e)
    {
        bool on = AutoSaveItem.IsChecked;
        App.Settings.Update(settings => settings with { AutoSave = on });
    }

    /// <summary>A legacy code page could not hold the text, so the file was saved as UTF-8: said once per document.</summary>
    private async Task WarnEncodingChangedAsync(DocumentFormat wanted, DocumentFormat written)
    {
        if (_warnedEncoding || wanted.Encoding == written.Encoding)
        {
            return;
        }

        _warnedEncoding = true;
        await FileDialogs.ShowErrorAsync(RootGrid.XamlRoot,
            UiStrings.Format("dialog.encodingFallback", DisplayName, EncodingDisplayName(wanted), EncodingDisplayName(written)));
    }

    private static string EncodingDisplayName(DocumentFormat format)
    {
        string name = MarkdownFileCodec.NameOf(format);
        const string BomSuffix = " with BOM";
        return name.EndsWith(BomSuffix, StringComparison.Ordinal)
            ? UiStrings.Format("file.encoding.withBom", name[..^BomSuffix.Length])
            : name;
    }
}
