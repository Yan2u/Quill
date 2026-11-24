using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Shapes;
using Quill.Editor.WinUI.Localization;
using Quill.Editor.WinUI.Theming;

namespace Quill.Shell.Themes;

/// <summary>
/// A theme in the theme list (15-themes.md): the name, the description and the dates at the left, a short
/// document rendered with the theme at the right, fading in from the left. Copy is offered for every theme,
/// Delete for user themes not in use; clicking the card (or Edit / View) opens its configuration page.
/// </summary>
public sealed partial class ThemeCard : UserControl
{
    private readonly ThemePreview _preview = new(ThemePreview.CardSample) { CanScroll = false };
    private readonly TextBlock _name;
    private readonly TextBlock _description;
    private readonly TextBlock _dates;
    private bool _canDelete;

    public ThemeCard(ThemeInfo theme, bool canDelete)
    {
        Theme = theme;
        _canDelete = canDelete;
        _preview.Sheet = theme.Sheet;
        _name = new TextBlock { Style = StyleOf("SubtitleTextBlockStyle"), TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 2, TextWrapping = TextWrapping.Wrap };
        _description = Themed.Load<TextBlock>("TextBlock", "TextWrapping='Wrap' MaxLines='3' TextTrimming='CharacterEllipsis' Foreground='{ThemeResource TextFillColorSecondaryBrush}'");
        _dates = Themed.Load<TextBlock>("TextBlock", "TextWrapping='Wrap' Foreground='{ThemeResource TextFillColorTertiaryBrush}'");
        _dates.Style = StyleOf("CaptionTextBlockStyle");

        OpenButton = new Button();
        CopyButton = new Button();
        DeleteButton = new Button();

        OpenButton.Click += (_, _) => Opened?.Invoke(this, EventArgs.Empty);
        CopyButton.Click += (_, _) => CopyRequested?.Invoke(this, EventArgs.Empty);
        DeleteButton.Click += (_, _) => DeleteRequested?.Invoke(this, EventArgs.Empty);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { OpenButton, CopyButton, DeleteButton } };
        var text = new Grid { RowSpacing = 6, Padding = new Thickness(20, 16, 12, 16) };
        text.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        text.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        text.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        text.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        text.Children.Add(_name);
        Grid.SetRow(_description, 1);
        text.Children.Add(_description);
        Grid.SetRow(_dates, 2);
        text.Children.Add(_dates);
        Grid.SetRow(actions, 3);
        actions.Margin = new Thickness(0, 8, 0, 0);
        text.Children.Add(actions);

        // The preview fades in from the left: a gradient of the card's color over its left part.
        var fade = Themed.Load<Rectangle>("Rectangle", "IsHitTestVisible='False'");
        fade.Fill = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Markup.XamlReader.Load(
            "<LinearGradientBrush xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' StartPoint='0,0' EndPoint='1,0'>"
            + "<GradientStop Offset='0' Color='{ThemeResource SolidBackgroundFillColorTertiary}' />"
            + "<GradientStop Offset='0.45' Color='#00FFFFFF' /></LinearGradientBrush>");
        var preview = new Grid { Children = { _preview, fade } };

        var layout = new Grid { ColumnSpacing = 0 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MinWidth = 160 });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        layout.Children.Add(text);
        Grid.SetColumn(preview, 1);
        layout.Children.Add(preview);

        var card = Themed.Load<Border>("Border",
            "Height='220' CornerRadius='8' BorderThickness='1' BorderBrush='{ThemeResource CardStrokeColorDefaultBrush}' Background='{ThemeResource SolidBackgroundFillColorTertiaryBrush}'");
        card.Child = layout;
        Content = card;
        RefreshText();
        Tapped += OnTapped;
    }

    /// <summary>The card was clicked, or Edit / View: open the theme's configuration page.</summary>
    public event EventHandler? Opened;

    public event EventHandler? CopyRequested;

    public event EventHandler? DeleteRequested;

    public ThemeInfo Theme { get; private set; }

    internal Button OpenButton { get; }

    internal Button CopyButton { get; }

    internal Button DeleteButton { get; }

    internal ThemePreview Preview => _preview;

    internal void Update(ThemeInfo theme, bool canDelete)
    {
        Theme = theme;
        _canDelete = canDelete;
        if (_preview.Sheet != theme.Sheet)
        {
            _preview.Sheet = theme.Sheet;
        }

        RefreshText();
    }

    internal void SetCanDelete(bool canDelete)
    {
        _canDelete = canDelete;
        UpdateDeleteState();
    }

    internal void RefreshLanguage() => RefreshText();

    internal bool Matches(string query) =>
        query.Length == 0
        || Theme.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || ResolvedDescription().Contains(query, StringComparison.CurrentCultureIgnoreCase);

    internal void Release()
    {
        _preview.Release();
        Content = null;
    }

    private string ResolvedDescription() => Theme.IsBuiltIn
        ? BuiltInThemes.Find(Theme.Id)?.LocalizedDescription ?? Theme.Description
        : Theme.Description;

    private void RefreshText()
    {
        _name.Text = Theme.Name;
        string resolvedDescription = ResolvedDescription();
        _description.Text = resolvedDescription.Length > 0
            ? resolvedDescription
            : Theme.IsBuiltIn ? UiStrings.Get("themes.card.builtInFallback") : UiStrings.Get("themes.card.noDescription");
        _dates.Text = Theme.IsBuiltIn
            ? UiStrings.Get("themes.card.builtIn")
            : UiStrings.Format("themes.card.dates",
                Theme.Created.LocalDateTime.ToString("d", UiStrings.Culture),
                Theme.Modified.LocalDateTime.ToString("g", UiStrings.Culture));

        string openLabel = Theme.IsBuiltIn ? UiStrings.Get("themes.card.view") : UiStrings.Get("themes.card.edit");
        string copyLabel = UiStrings.Get("themes.card.copy");
        string deleteLabel = UiStrings.Get("themes.card.delete");
        OpenButton.Content = openLabel;
        CopyButton.Content = copyLabel;
        DeleteButton.Content = deleteLabel;
        AutomationProperties.SetName(OpenButton, $"{openLabel} {Theme.Name}");
        AutomationProperties.SetName(CopyButton, $"{copyLabel} {Theme.Name}");
        AutomationProperties.SetName(DeleteButton, $"{deleteLabel} {Theme.Name}");
        AutomationProperties.SetName(this, Theme.Name);
        UpdateDeleteState();
    }

    private void UpdateDeleteState()
    {
        DeleteButton.Visibility = Theme.IsBuiltIn ? Visibility.Collapsed : Visibility.Visible;
        DeleteButton.IsEnabled = _canDelete;
        ToolTipService.SetToolTip(DeleteButton, !Theme.IsBuiltIn && !_canDelete ? UiStrings.Get("themes.card.inUseTip") : null);
    }

    private static Style StyleOf(string key) => (Style)Application.Current.Resources[key];

    private void OnTapped(object sender, TappedRoutedEventArgs e)
    {
        // The buttons take their own clicks.
        for (var element = e.OriginalSource as DependencyObject; element is not null && !ReferenceEquals(element, this); element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element))
        {
            if (element is ButtonBase)
            {
                return;
            }
        }

        Opened?.Invoke(this, EventArgs.Empty);
    }
}
