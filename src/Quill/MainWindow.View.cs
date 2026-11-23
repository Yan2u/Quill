using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Animation = Microsoft.UI.Xaml.Media.Animation;
using Quill.Editor.Statistics;
using Quill.Editor.WinUI.Localization;
using Quill.Shell.Sidebar;
using Quill.StatusBar;
using VirtualKey = Windows.System.VirtualKey;
using VirtualKeyModifiers = Windows.System.VirtualKeyModifiers;

namespace Quill;

/// <summary>
/// The outline sidebar and the word count (M6.5). Both follow the document a moment after edits stop,
/// computed in the background (each recount covers only the leaves an edit changed, the rest come from a
/// cache; a result for an older document is dropped); the outline's
/// current heading follows the scroll position (Typora). View → Sidebar (Ctrl+Shift+L) and the
/// title bar's sidebar button show the sidebar: Outline and Files tabs.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(150);

    private readonly OutlinePanel _outline = new();
    private readonly FilesPanel _files = new() { IsActive = false };
    private SidebarPanel? _sidebar;
    private readonly WordCountButton _wordCount = new() { HorizontalAlignment = HorizontalAlignment.Right };
    private DispatcherQueueTimer? _refresh;
    private ToggleMenuFlyoutItem? _outlineItem;

    /// <summary>The outline panel, for tests.</summary>
    internal OutlinePanel Outline => _outline;

    private void InitializeView()
    {
        _sidebar = new SidebarPanel(_outline, _files);
        _sidebar.TabChanged += (_, _) =>
        {
            UpdateFilesPanelActivity();
            RefreshView();
        };
        SidebarHost.Child = _sidebar;
        _files.FileInvoked += async (_, path) =>
        {
            if (!string.Equals(path, _path, StringComparison.OrdinalIgnoreCase) && await ConfirmSwitchAsync())
            {
                await OpenFileAsync(path);
            }
        };
        _files.OpenFolderRequested += async (_, _) => await OpenFolderAsync();
        Closed += (_, _) => _files.Dispose();
        StatusBarHost.Children.Add(_wordCount);
        _outline.HeadingInvoked += (_, heading) => _editor.GoToHeading(heading);

        // After the editor's menus, as in Typora: File, Edit, Paragraph, Format, View.
        _outlineItem = new ToggleMenuFlyoutItem { Text = UiStrings.Get("view.sidebar") };
        _outlineItem.KeyboardAccelerators.Add(new KeyboardAccelerator { Modifiers = VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, Key = VirtualKey.L });
        _outlineItem.Click += (_, _) => ShowSidebar(_outlineItem.IsChecked);
        _menus.Append("View", new MenuFlyoutSeparator());
        _menus.Append("View", _outlineItem);

        _refresh = DispatcherQueue.CreateTimer();
        _refresh.Interval = RefreshDelay;
        _refresh.IsRepeating = false;
        _refresh.Tick += (_, _) => RefreshView();
        _editor.Changed += (_, _) => _refresh.Start();
        _editor.ViewportChanged += (_, _) => UpdateCurrentHeading();
        RefreshView();
    }

    /// <summary>
    /// Shows or hides the sidebar, animated: it slides in from the left edge of the card and fades in, or
    /// slides out and fades before it collapses. The editor takes the new width once, at the start of showing
    /// and at the end of hiding, not on every frame (each width would lay out the visible blocks again, 09).
    /// </summary>
    private void ShowSidebar(bool show)
    {
        AnimateSidebar(show);
        UpdateFilesPanelActivity();
        SidebarSizer.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (_outlineItem is not null)
        {
            _outlineItem.IsChecked = show;
        }

        if (show)
        {
            RefreshView();
        }

        StateChanged();
    }

    private void UpdateFilesPanelActivity() =>
        _files.IsActive = IsSidebarShown && _sidebar is { ShowsOutline: false };

    private Animation.Storyboard? _sidebarAnimation;

    /// <summary>Whether the sidebar is shown or showing (it stays visible while it slides out).</summary>
    private bool IsSidebarShown { get; set; }

    private void AnimateSidebar(bool show)
    {
        IsSidebarShown = show;
        _sidebarAnimation?.Stop();
        bool visible = SidebarHost.Visibility == Visibility.Visible;
        if (show == visible && _sidebarAnimation is null)
        {
            return;
        }

        if (SidebarHost.RenderTransform is not TranslateTransform slide)
        {
            slide = new TranslateTransform();
            SidebarHost.RenderTransform = slide;
        }

        SidebarHost.Visibility = Visibility.Visible;
        var duration = TimeSpan.FromMilliseconds(show ? 250 : 160);
        var ease = new Animation.CubicEase { EasingMode = show ? Animation.EasingMode.EaseOut : Animation.EasingMode.EaseIn };
        var move = new Animation.DoubleAnimation { To = show ? 0 : -SidebarDistance, Duration = duration, EasingFunction = ease };
        var fade = new Animation.DoubleAnimation { To = show ? 1 : 0, Duration = duration, EasingFunction = ease };
        if (!visible)
        {
            slide.X = -SidebarDistance;
            SidebarHost.Opacity = 0;
        }

        Animation.Storyboard.SetTarget(move, slide);
        Animation.Storyboard.SetTargetProperty(move, "X");
        Animation.Storyboard.SetTarget(fade, SidebarHost);
        Animation.Storyboard.SetTargetProperty(fade, "Opacity");
        var storyboard = new Animation.Storyboard { Children = { move, fade } };
        storyboard.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_sidebarAnimation, storyboard))
            {
                return;
            }

            _sidebarAnimation = null;
            if (!show)
            {
                SidebarHost.Visibility = Visibility.Collapsed;
            }
        };
        _sidebarAnimation = storyboard;
        storyboard.Begin();
    }

    /// <summary>Asks for a folder and shows it in the sidebar's Files tab.</summary>
    private async Task OpenFolderAsync()
    {
        if (await Files.FileDialogs.PickFolderAsync(this) is { } folder)
        {
            ShowFolder(folder);
        }
    }

    /// <summary>Shows a folder in the sidebar's Files tab.</summary>
    private void ShowFolder(string folder)
    {
        _files.OpenFolder(folder);
        if (_sidebar is not null)
        {
            _sidebar.Tab = SidebarTab.Files;
        }

        ShowPage(Shell.Pages.ShellPage.Editor);
        ShowSidebar(true);
        StateChanged();
    }

    /// <summary>How far the sidebar slides.</summary>
    private const double SidebarDistance = 48;

    /// <summary>
    /// Brings the word count and (when shown) the outline up to date (09: counted in the background, the
    /// result dropped when the document changed meanwhile; the next refresh brings the newer one).
    /// </summary>
    private async void RefreshView()
    {
        if (!_editor.HasDocument)
        {
            return;
        }

        var state = _editor.State;
        bool outline = IsSidebarShown && _sidebar is { ShowsOutline: true };
        var result = await Task.Run(() => (
            Total: WordStatistics.Of(state.Document),
            Selected: WordStatistics.OfSelection(state),
            Outline: outline ? DocumentOutline.Of(state.Document) : null));
        if (!_editor.HasDocument || !ReferenceEquals(_editor.State.Document, state.Document))
        {
            return;
        }

        _wordCount.Show(result.Total, ReferenceEquals(_editor.State.Selection, state.Selection) ? result.Selected : WordStatistics.OfSelection(_editor.State));
        if (result.Outline is { } entries)
        {
            _outline.Update(entries);
            UpdateCurrentHeading();
        }
    }

    private void UpdateCurrentHeading()
    {
        if (!IsSidebarShown || _sidebar is not { ShowsOutline: true } || !_editor.HasDocument)
        {
            return;
        }

        _outline.SetCurrent(_editor.FirstVisibleBlock() is { } block
            ? DocumentOutline.CurrentHeading(_editor.State.Document, _outline.Entries, block)
            : null);
    }
}
