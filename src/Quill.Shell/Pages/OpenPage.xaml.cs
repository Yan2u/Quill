using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Quill.Editor.WinUI.Localization;

namespace Quill.Shell.Pages;

/// <summary>A row of the recent files: the file name and its folder.</summary>
public sealed record RecentFileItem(string Path)
{
    public string Name => System.IO.Path.GetFileName(Path);

    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
}

/// <summary>
/// The Open page (after Typora's File → Open): open a file, import (not available yet), and the recent
/// files with a search box. The window opens what is asked for and returns to the editor.
/// </summary>
public sealed partial class OpenPage : UserControl
{
    private IReadOnlyList<string> _paths = [];

    public OpenPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            UiStrings.LanguageChanged -= OnLanguageChanged;
            UiStrings.LanguageChanged += OnLanguageChanged;
            ApplyLanguage();
        };
        Unloaded += (_, _) => UiStrings.LanguageChanged -= OnLanguageChanged;
        ApplyLanguage();
    }

    /// <summary>"Open file..." was clicked.</summary>
    public event EventHandler? OpenRequested;

    /// <summary>A recent file was clicked.</summary>
    public event EventHandler<string>? RecentRequested;

    /// <summary>"Clear recent files" was clicked.</summary>
    public event EventHandler? ClearRequested;

    /// <summary>Shows the recent files, most recent first.</summary>
    public void SetRecent(IReadOnlyList<string> paths)
    {
        _paths = paths;
        Filter();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyLanguage();

    private void ApplyLanguage()
    {
        TitleText.Text = UiStrings.Get("open.title");
        AutomationProperties.SetName(OpenFileButton, UiStrings.Get("open.openFileButton"));
        OpenFileText.Text = UiStrings.Get("open.openFileText");
        ImportHeading.Text = UiStrings.Get("open.importHeading");
        AutomationProperties.SetName(ImportButton, UiStrings.Get("open.importButton"));
        ImportText.Text = UiStrings.Get("open.importText");
        ImportUnavailableText.Text = UiStrings.Get("open.importUnavailable");
        RecentHeading.Text = UiStrings.Get("open.recentHeading");
        SearchBox.PlaceholderText = UiStrings.Get("open.searchPlaceholder");
        AutomationProperties.SetName(SearchBox, UiStrings.Get("open.searchName"));
        string more = UiStrings.Get("open.more");
        ToolTipService.SetToolTip(MoreButton, more);
        AutomationProperties.SetName(MoreButton, more);
        ClearItem.Text = UiStrings.Get("open.clearRecent");
        ColumnNameText.Text = UiStrings.Get("open.columnName");
        ColumnFolderText.Text = UiStrings.Get("open.columnFolder");
        Filter();
    }

    private void Filter()
    {
        string query = SearchBox.Text.Trim();
        var items = _paths
            .Where(path => query.Length == 0 || path.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(static path => new RecentFileItem(path))
            .ToList();
        RecentList.ItemsSource = items;
        EmptyText.Text = _paths.Count == 0 ? UiStrings.Get("open.empty") : UiStrings.Get("open.emptyMatch");
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearItem.IsEnabled = _paths.Count > 0;
    }

    private void OnOpenClick(object sender, RoutedEventArgs e) => OpenRequested?.Invoke(this, EventArgs.Empty);

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => Filter();

    private void OnClearClick(object sender, RoutedEventArgs e) => ClearRequested?.Invoke(this, EventArgs.Empty);

    private void OnRecentClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is RecentFileItem item)
        {
            RecentRequested?.Invoke(this, item.Path);
        }
    }
}
