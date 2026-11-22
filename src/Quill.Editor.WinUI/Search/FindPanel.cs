using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Quill.Editor.Documents;
using Quill.Editor.Search;
using Quill.Editor.State;
using Quill.Editor.WinUI.Localization;
using Windows.System;
using Windows.UI.Core;

namespace Quill.Editor.WinUI.Search;

/// <summary>
/// The find panel of a <see cref="MarkdownBox"/> (12-find-replace.md), in the Windows 11 style and laid
/// out like Notepad's: the find box with "n of m", previous, next, the options menu (match case, whole
/// word, regular expression) and close; the replace row with Replace and Replace all. The host puts it
/// over the editor (top right, as a sibling of the box, so its keys never reach the editor); it opens on
/// the editor's Ctrl+F and Ctrl+H. Enter and F3 go to the next match, Shift+Enter and Shift+F3 to the
/// previous one, Esc closes the panel and gives the focus back to the editor.
/// </summary>
public sealed partial class FindPanel : UserControl
{
    private const double ButtonSize = 32;

    private static readonly FontFamily IconFont = new("Segoe Fluent Icons,Segoe MDL2 Assets");

    private readonly MarkdownBox _editor;
    private readonly TextBox _find = new() { Width = 220, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _replace = new() { Width = 220, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { MinWidth = 72, Margin = new Thickness(8, 0, 4, 0), Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 160 };
    private readonly ToggleMenuFlyoutItem _matchCase = new();
    private readonly ToggleMenuFlyoutItem _wholeWord = new();
    private readonly ToggleMenuFlyoutItem _regex = new();
    private readonly StackPanel _replaceRow = new() { Orientation = Orientation.Horizontal, Spacing = 4, Visibility = Visibility.Collapsed, Margin = new Thickness(ButtonSize + 4, 4, 0, 0) };
    private readonly Button _expand;
    private readonly Button _previous;
    private readonly Button _next;
    private readonly Button _options;
    private readonly Button _close;
    private readonly Button _replaceOne;
    private readonly Button _replaceAll;

    public FindPanel(MarkdownBox editor)
    {
        _editor = editor;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Top;
        Margin = new Thickness(0, 8, 24, 0);
        Visibility = Visibility.Collapsed;

        _expand = Icon("", UiStrings.Get("find.toggleReplace"));
        _expand.Click += (_, _) => ShowReplace(_replaceRow.Visibility != Visibility.Visible);
        _previous = Icon("", UiStrings.Get("find.previous"));
        _previous.Click += (_, _) => _editor.Search.FindNext(forward: false);
        _next = Icon("", UiStrings.Get("find.next"));
        _next.Click += (_, _) => _editor.Search.FindNext(forward: true);
        _options = Icon("", UiStrings.Get("find.options"));
        var menu = new MenuFlyout();
        foreach (var option in new[] { _matchCase, _wholeWord, _regex })
        {
            option.Click += (_, _) => Search();
            menu.Items.Add(option);
        }

        _options.Flyout = menu;
        _close = Icon("", UiStrings.Get("find.close"));
        _close.Click += (_, _) => Close();

        var findRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        findRow.Children.Add(_expand);
        findRow.Children.Add(_find);
        findRow.Children.Add(_status);
        findRow.Children.Add(_previous);
        findRow.Children.Add(_next);
        findRow.Children.Add(_options);
        findRow.Children.Add(_close);

        _replaceOne = new Button { VerticalAlignment = VerticalAlignment.Center };
        _replaceOne.Click += (_, _) => _editor.Search.ReplaceCurrent(_replace.Text);
        _replaceAll = new Button { VerticalAlignment = VerticalAlignment.Center };
        _replaceAll.Click += (_, _) => _editor.Search.ReplaceAll(_replace.Text);
        _replaceRow.Children.Add(_replace);
        _replaceRow.Children.Add(_replaceOne);
        _replaceRow.Children.Add(_replaceAll);

        var rows = new StackPanel();
        rows.Children.Add(findRow);
        rows.Children.Add(_replaceRow);
        var frame = Frame();
        frame.Child = rows;
        Content = frame;

        _find.TextChanged += (_, _) => Search();
        _find.KeyDown += OnFindKeyDown;
        _replace.KeyDown += OnReplaceKeyDown;
        _editor.FindRequested += (_, replace) => Open(replace);
        _editor.Search.Changed += (_, _) => UpdateStatus();
        Loaded += (_, _) =>
        {
            UiStrings.LanguageChanged -= OnLanguageChanged;
            UiStrings.LanguageChanged += OnLanguageChanged;
            ApplyLanguage();
        };
        Unloaded += (_, _) => UiStrings.LanguageChanged -= OnLanguageChanged;
        ApplyLanguage();
    }

    public bool IsOpen => Visibility == Visibility.Visible;

    public bool IsReplaceShown => _replaceRow.Visibility == Visibility.Visible;

    public TextBox FindBox => _find;

    public TextBox ReplaceBox => _replace;

    /// <summary>The status shown next to the find box: "n of m", "m results", "No results" or the expression's error.</summary>
    public string Status => _status.Text;

    public bool MatchCase
    {
        get => _matchCase.IsChecked;
        set
        {
            _matchCase.IsChecked = value;
            Search();
        }
    }

    public bool WholeWord
    {
        get => _wholeWord.IsChecked;
        set
        {
            _wholeWord.IsChecked = value;
            Search();
        }
    }

    public bool UseRegex
    {
        get => _regex.IsChecked;
        set
        {
            _regex.IsChecked = value;
            Search();
        }
    }

    /// <summary>
    /// Opens the panel (Ctrl+F; with the replace row for Ctrl+H). A selection within one line of a block
    /// fills the find box〔默认〕; the search starts from the selection.
    /// </summary>
    public void Open(bool replace)
    {
        Visibility = Visibility.Visible;
        ShowReplace(replace);
        _editor.Search.Begin();
        if (SelectedText() is { } selected && selected != _find.Text)
        {
            _find.Text = selected;
        }
        else
        {
            Search();
        }

        _find.Focus(FocusState.Programmatic);
        _find.SelectAll();
    }

    /// <summary>Closes the panel: the matches are no longer drawn and the editor has the focus again.</summary>
    public void Close()
    {
        Visibility = Visibility.Collapsed;
        _editor.Search.Clear();
        _editor.Focus(FocusState.Programmatic);
    }

    private void ShowReplace(bool shown)
    {
        _replaceRow.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        ((FontIcon)_expand.Content).Glyph = shown ? "" : "";
    }

    private void Search()
    {
        if (IsOpen)
        {
            _editor.Search.SetQuery(_find.Text.Length == 0 ? null : new SearchQuery(_find.Text, _matchCase.IsChecked, _wholeWord.IsChecked, _regex.IsChecked));
        }
    }

    private void UpdateStatus()
    {
        var search = _editor.Search;
        _status.Text = search.Error is not null ? UiStrings.Get("find.invalidRegex")
            : search.Query is null ? string.Empty
            : search.Count == 0 ? UiStrings.Get("find.noResults")
            : search.Current > 0 ? UiStrings.Format("find.matchPosition", search.Current, search.Count)
            : search.Count == 1 ? UiStrings.Get("find.singleResult") : UiStrings.Format("find.manyResults", search.Count);
        ToolTipService.SetToolTip(_status, search.Error);
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyLanguage();

    private void ApplyLanguage()
    {
        _find.PlaceholderText = UiStrings.Get("find.placeholder");
        _replace.PlaceholderText = UiStrings.Get("find.replacePlaceholder");
        _matchCase.Text = UiStrings.Get("find.matchCase");
        _wholeWord.Text = UiStrings.Get("find.wholeWord");
        _regex.Text = UiStrings.Get("find.regex");
        _replaceOne.Content = UiStrings.Get("find.replaceOne");
        _replaceAll.Content = UiStrings.Get("find.replaceAll");
        SetTip(_expand, "find.toggleReplace");
        SetTip(_previous, "find.previous");
        SetTip(_next, "find.next");
        SetTip(_options, "find.options");
        SetTip(_close, "find.close");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_find, UiStrings.Get("find.placeholder"));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_replace, UiStrings.Get("find.replaceName"));
        UpdateStatus();
    }

    private static void SetTip(Button button, string key)
    {
        string tip = UiStrings.Get(key);
        ToolTipService.SetToolTip(button, tip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tip);
    }

    /// <summary>The selection's text when it lies within one line of one block (and is not long).</summary>
    private string? SelectedText()
    {
        if (!_editor.HasDocument || _editor.State.Selection.IsCollapsed)
        {
            return null;
        }

        var (start, end) = PositionOrder.Ordered(_editor.State.Document, _editor.State.Selection);
        if (start is not TextPosition s || end is not TextPosition e || s.Block != e.Block || DocumentLookup.Find(_editor.State.Document, s.Block) is not LeafBlock leaf)
        {
            return null;
        }

        string text = leaf.Text[s.Offset..e.Offset];
        return text.Length is > 0 and <= 200 && !text.Contains('\n') ? text : null;
    }

    private void OnFindKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool shift = IsDown(VirtualKey.Shift);
        bool control = IsDown(VirtualKey.Control);
        switch (e.Key)
        {
            case VirtualKey.Enter or VirtualKey.F3:
                _editor.Search.FindNext(forward: !shift);
                break;
            case VirtualKey.Escape:
                Close();
                break;
            case VirtualKey.H when control:
                ShowReplace(!IsReplaceShown);
                break;
            case VirtualKey.F when control:
                _find.SelectAll();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnReplaceKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                _editor.Search.ReplaceCurrent(_replace.Text);
                break;
            case VirtualKey.F3:
                _editor.Search.FindNext(forward: !IsDown(VirtualKey.Shift));
                break;
            case VirtualKey.Escape:
                Close();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private static bool IsDown(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private static Button Icon(string glyph, string tip)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 14, FontFamily = IconFont },
            Width = ButtonSize,
            Height = ButtonSize,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(button, tip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tip);
        return button;
    }

    /// <summary>The panel's surface: in-app acrylic with the flyout stroke and corner radius, following the theme.</summary>
    private static Border Frame() => (Border)XamlReader.Load("""
        <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                Background="{ThemeResource AcrylicInAppFillColorDefaultBrush}"
                BorderBrush="{ThemeResource SurfaceStrokeColorFlyoutBrush}"
                BorderThickness="1"
                CornerRadius="{ThemeResource OverlayCornerRadius}"
                Padding="6" />
        """);
}
