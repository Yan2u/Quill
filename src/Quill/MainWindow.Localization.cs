using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Quill.Editor.WinUI.Localization;

namespace Quill;

/// <summary>
/// Live localization updates for the window chrome, File menu, NavigationView and active editor surface.
/// </summary>
public sealed partial class MainWindow
{
    private void InitializeLocalization()
    {
        UiStrings.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => UiStrings.LanguageChanged -= OnLanguageChanged;
        ApplyLanguage();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            ApplyLanguage();
        }
        else
        {
            DispatcherQueue.TryEnqueue(ApplyLanguage);
        }
    }

    private void ApplyLanguage()
    {
        SetTipAndName(NavigationButton, "nav.toggle", "nav.toggle");
        SetTipAndName(UndoButton, "nav.undo", "nav.undoName");
        SetTipAndName(RedoButton, "nav.redo", "nav.redoName");
        SetTipAndName(SidebarButton, "nav.sidebarTip", "nav.sidebarName");

        FileMenu.Title = UiStrings.Get("file.menu");
        NewWindowItem.Text = UiStrings.Get("file.newWindow");
        OpenFileItem.Text = UiStrings.Get("file.open");
        OpenFolderItem.Text = UiStrings.Get("file.openFolder");
        RecentMenu.Text = UiStrings.Get("file.openRecent");
        EncodingMenu.Text = UiStrings.Get("file.reopenEncoding");
        UpdateSystemEncodingLabel();
        SaveItem.Text = UiStrings.Get("file.save");
        SaveAsItem.Text = UiStrings.Get("file.saveAs");
        ExportItem.Text = UiStrings.Get("file.export");
        AutoSaveItem.Text = UiStrings.Get("file.autoSave");
        ExitItem.Text = UiStrings.Get("file.exit");
        BuildRecentMenu();

        HomeItem.Content = UiStrings.Get("nav.home");
        OpenNavItem.Content = UiStrings.Get("nav.open");
        ThemeNavItem.Content = UiStrings.Get("nav.theme");
        AboutNavItem.Content = UiStrings.Get("nav.about");
        SettingsNavItem.Content = UiStrings.Get("nav.settings");
        AutomationProperties.SetName(SidebarSizer, UiStrings.Get("sidebar.widthName"));

        if (_outlineItem is not null)
        {
            _outlineItem.Text = UiStrings.Get("view.sidebar");
        }

        _menus?.Refresh();
        UpdateTitle();
        _editor.RefreshLanguage();

        if (PageHost.Children.FirstOrDefault() is Shell.Pages.AboutPage about)
        {
            about.RefreshLanguage();
        }
    }

    private static void SetTipAndName(Button button, string tipKey, string nameKey)
    {
        ToolTipService.SetToolTip(button, UiStrings.Get(tipKey));
        AutomationProperties.SetName(button, UiStrings.Get(nameKey));
    }
}
