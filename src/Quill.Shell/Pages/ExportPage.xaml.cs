using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Quill.Editor.WinUI.Localization;

namespace Quill.Shell.Pages;

/// <summary>
/// The Export page (after Typora's File → Export): a placeholder listing the formats, none of which
/// exports yet.
/// </summary>
public sealed partial class ExportPage : UserControl
{
    private static readonly (string Glyph, string Name, string? Key)[] Main =
    [
        ("", "HTML", null),
        ("", "HTML (without styles)", "export.format.htmlUnstyled"),
        ("", "PDF", null),
    ];

    private static readonly (string Glyph, string Name)[] More =
    [
        ("", "Word (.docx)"),
        ("", "OpenOffice"),
        ("", "RTF"),
        ("", "Epub"),
        ("", "LaTeX"),
        ("", "MediaWiki"),
        ("", "reStructuredText"),
        ("", "Textile"),
        ("", "OPML"),
    ];

    private readonly List<Action> _localizers = [];

    public ExportPage()
    {
        InitializeComponent();
        foreach (var (glyph, name, key) in Main)
        {
            Formats.Children.Add(Card(glyph, name, key));
        }

        foreach (var (glyph, name) in More)
        {
            MoreFormats.Children.Add(Card(glyph, name, null));
        }

        Loaded += (_, _) =>
        {
            UiStrings.LanguageChanged -= OnLanguageChanged;
            UiStrings.LanguageChanged += OnLanguageChanged;
            ApplyLanguage();
        };
        Unloaded += (_, _) => UiStrings.LanguageChanged -= OnLanguageChanged;
        ApplyLanguage();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyLanguage();

    private void ApplyLanguage()
    {
        TitleText.Text = UiStrings.Get("export.title");
        UnavailableBar.Message = UiStrings.Get("export.unavailable");
        MoreHeading.Text = UiStrings.Get("export.moreFormats");
        foreach (var localize in _localizers)
        {
            localize();
        }
    }

    private Button Card(string glyph, string name, string? key)
    {
        var label = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        content.Children.Add(new FontIcon { Glyph = glyph, FontSize = 18 });
        content.Children.Add(label);
        var button = new Button { Style = (Style)Resources["FormatCardStyle"], Content = content };
        AutomationProperties.SetName(button, name);
        if (key is not null)
        {
            _localizers.Add(() =>
            {
                string text = UiStrings.Get(key);
                label.Text = text;
                AutomationProperties.SetName(button, text);
            });
        }

        return button;
    }
}
