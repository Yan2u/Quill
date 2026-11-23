using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Quill.Editor.WinUI.Localization;
using Windows.Foundation;

namespace Quill.Shell.Pages;

/// <summary>
/// The About page: application identity card, project and developer links, and open-source project attributions.
/// </summary>
public sealed partial class AboutPage : UserControl
{
    public const string ProjectHomepageUrl = "https://github.com/yan2u/Quill";
    public const string DeveloperHomepageUrl = "https://github.com/yan2u";

    public static IReadOnlyList<(string Project, string Website)> OpenSourceProjects { get; } =
    [
        ("Microsoft.WindowsAppSDK (WinUI 3)", "https://github.com/microsoft/windowsappsdk"),
        ("Microsoft.Graphics.Win2D", "https://github.com/microsoft/Win2D"),
        ("CommunityToolkit.Windows", "https://github.com/CommunityToolkit/Windows"),
        ("DevWinUI", "https://github.com/ghost1372/DevWinUI"),
        ("Markdig", "https://github.com/xoofx/markdig"),
        ("TextMateSharp", "https://github.com/danipen/TextMateSharp"),
        ("Oniguruma", "https://github.com/kkos/oniguruma"),
        ("RaTeX.Net", "https://github.com/Sylinko/RaTeX.Net"),
        ("KaTeX (Fonts)", "https://github.com/KaTeX/KaTeX"),
        ("mermaid-rs-renderer", "https://github.com/1jehuang/mermaid-rs-renderer"),
        ("resvg (usvg)", "https://github.com/linebender/resvg"),
    ];

    private readonly string _version;
    private UiLanguage? _appliedLanguage;
    private readonly TextBlock _title = new() { Style = (Style)Application.Current.Resources["TitleTextBlockStyle"] };
    private readonly TextBlock _versionBlock = new()
    {
        IsTextSelectionEnabled = true,
        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
    };
    private readonly TextBlock _descriptionBlock = Themed.Load<TextBlock>(
        "TextBlock",
        "TextWrapping='Wrap' Foreground='{ThemeResource TextFillColorSecondaryBrush}'");

    private readonly TextBlock _linksHeading = new()
    {
        Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
        Margin = new Thickness(0, 8, 0, 0),
    };
    private readonly SettingsCard _projectCard;
    private readonly SettingsCard _developerCard;
    private readonly HyperlinkButton _projectLink;
    private readonly HyperlinkButton _developerLink;

    private readonly TextBlock _openSourceHeading = new()
    {
        Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
        Margin = new Thickness(0, 8, 0, 0),
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly TextBlock _projectColumnHeader = new()
    {
        Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
    };
    private readonly TextBlock _websiteColumnHeader = new()
    {
        Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
    };

    public AboutPage(string version, string? iconPath = null)
    {
        _version = version;

        var identityRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        if (iconPath is not null && File.Exists(iconPath))
        {
            identityRow.Children.Add(new Image
            {
                Width = 72,
                Height = 72,
                VerticalAlignment = VerticalAlignment.Center,
                Source = new BitmapImage(new Uri(iconPath)) { DecodePixelWidth = 144 },
            });
        }

        var identityText = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        identityText.Children.Add(new TextBlock
        {
            Text = "Quill",
            Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"],
        });
        identityText.Children.Add(_versionBlock);
        identityText.Children.Add(_descriptionBlock);
        identityRow.Children.Add(identityText);

        var heroCard = Themed.Load<Border>(
            "Border",
            "CornerRadius='8' BorderThickness='1' Padding='20' Background='{ThemeResource CardBackgroundFillColorDefaultBrush}' BorderBrush='{ThemeResource CardStrokeColorDefaultBrush}'");
        heroCard.Child = identityRow;

        _projectLink = LinkButton(ProjectHomepageUrl);
        _projectCard = new SettingsCard
        {
            HeaderIcon = new FontIcon { Glyph = "\uE774" },
            Content = _projectLink,
        };

        _developerLink = LinkButton(DeveloperHomepageUrl);
        _developerCard = new SettingsCard
        {
            HeaderIcon = new FontIcon { Glyph = "\uE77B" },
            Content = _developerLink,
        };

        var linksStack = new StackPanel { Spacing = 4 };
        linksStack.Children.Add(_projectCard);
        linksStack.Children.Add(_developerCard);

        var tableCard = BuildOpenSourceTable();

        var root = new StackPanel
        {
            MaxWidth = MaxContentWidth,
            Padding = new Thickness(40, 28, 40, 40),
            Spacing = 16,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        root.Children.Add(_title);
        root.Children.Add(heroCard);
        root.Children.Add(_linksHeading);
        root.Children.Add(linksStack);
        root.Children.Add(_openSourceHeading);
        root.Children.Add(tableCard);

        // Wrap root in a panel that caps and centers its arrange slot explicitly: in WinUI 3,
        // combining MaxWidth with HorizontalAlignment=Stretch centers DesiredSize inside the
        // viewport before clamping to MaxWidth, which shifts the box to the right on wide windows.
        var centeredHost = new CenteredContainer(MaxContentWidth);
        centeredHost.Children.Add(root);

        Content = new ScrollViewer
        {
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = centeredHost,
        };

        ApplyLanguage();
        Loaded += (_, _) =>
        {
            UiStrings.LanguageChanged -= OnLanguageChanged;
            UiStrings.LanguageChanged += OnLanguageChanged;
            if (_appliedLanguage != UiStrings.Language)
            {
                ApplyLanguage();
            }
        };
        Unloaded += (_, _) =>
        {
            if (!IsLoaded)
            {
                UiStrings.LanguageChanged -= OnLanguageChanged;
            }
        };
    }

    public void RefreshLanguage() => ApplyLanguage();

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyLanguage();

    private void ApplyLanguage()
    {
        _appliedLanguage = UiStrings.Language;
        _title.Text = UiStrings.Get("about.title");
        _versionBlock.Text = UiStrings.Format("about.version", _version);
        _descriptionBlock.Text = UiStrings.Get("about.description");

        _linksHeading.Text = UiStrings.Get("about.linksHeading");

        string projectHeader = UiStrings.Get("about.projectHomepage");
        _projectCard.Header = projectHeader;
        _projectCard.Description = UiStrings.Get("about.projectHomepageDesc");
        AutomationProperties.SetName(_projectCard, projectHeader);
        AutomationProperties.SetName(_projectLink, projectHeader);

        string developerHeader = UiStrings.Get("about.developerHomepage");
        _developerCard.Header = developerHeader;
        _developerCard.Description = UiStrings.Get("about.developerHomepageDesc");
        AutomationProperties.SetName(_developerCard, developerHeader);
        AutomationProperties.SetName(_developerLink, developerHeader);

        _openSourceHeading.Text = UiStrings.Get("about.openSourceHeading");
        _projectColumnHeader.Text = UiStrings.Get("about.column.project");
        _websiteColumnHeader.Text = UiStrings.Get("about.column.website");
    }

    private Border BuildOpenSourceTable()
    {
        var tableFrame = Themed.Load<Border>(
            "Border",
            "CornerRadius='8' BorderThickness='1' Background='{ThemeResource CardBackgroundFillColorDefaultBrush}' BorderBrush='{ThemeResource CardStrokeColorDefaultBrush}'");
        var rows = new StackPanel();

        var headerGrid = TableColumnsGrid();
        headerGrid.Children.Add(_projectColumnHeader);
        Grid.SetColumn(_websiteColumnHeader, 1);
        headerGrid.Children.Add(_websiteColumnHeader);

        var headerBorder = Themed.Load<Border>(
            "Border",
            "Padding='16,10,16,10' CornerRadius='8,8,0,0' Background='{ThemeResource CardBackgroundFillColorSecondaryBrush}' BorderBrush='{ThemeResource DividerStrokeColorDefaultBrush}' BorderThickness='0,0,0,1'");
        headerBorder.Child = headerGrid;
        rows.Children.Add(headerBorder);

        for (int i = 0; i < OpenSourceProjects.Count; i++)
        {
            var (project, website) = OpenSourceProjects[i];
            bool last = i == OpenSourceProjects.Count - 1;
            string dividerAttr = last
                ? "Padding='16,8,16,8'"
                : "Padding='16,8,16,8' BorderBrush='{ThemeResource DividerStrokeColorDefaultBrush}' BorderThickness='0,0,0,1'";

            var rowGrid = TableColumnsGrid();
            var nameBlock = new TextBlock
            {
                Text = project,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
            };
            var link = LinkButton(website);
            AutomationProperties.SetName(link, $"{project} {website}");
            Grid.SetColumn(link, 1);
            rowGrid.Children.Add(nameBlock);
            rowGrid.Children.Add(link);

            var rowBorder = Themed.Load<Border>("Border", dividerAttr);
            rowBorder.Child = rowGrid;
            rows.Children.Add(rowBorder);
        }

        tableFrame.Child = rows;
        return tableFrame;
    }

    private static Grid TableColumnsGrid()
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        return grid;
    }

    private const double MaxContentWidth = 960;

    private static HyperlinkButton LinkButton(string url) => new()
    {
        Content = url,
        NavigateUri = new Uri(url),
        Padding = new Thickness(4, 2, 4, 2),
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private sealed partial class CenteredContainer(double maxContentWidth) : Panel
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            if (Children.Count == 0)
            {
                return default;
            }

            double constrainedWidth = double.IsFinite(availableSize.Width)
                ? Math.Min(availableSize.Width, maxContentWidth)
                : maxContentWidth;
            Children[0].Measure(new Size(constrainedWidth, availableSize.Height));
            double desiredWidth = double.IsFinite(availableSize.Width)
                ? availableSize.Width
                : Math.Min(Children[0].DesiredSize.Width, maxContentWidth);
            return new Size(desiredWidth, Children[0].DesiredSize.Height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (Children.Count > 0)
            {
                double contentWidth = Math.Min(finalSize.Width, maxContentWidth);
                double x = Math.Max(0, (finalSize.Width - contentWidth) / 2);
                Children[0].Arrange(new Rect(x, 0, contentWidth, finalSize.Height));
            }

            return finalSize;
        }
    }
}
