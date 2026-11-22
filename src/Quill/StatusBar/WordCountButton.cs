using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Quill.Editor.Statistics;
using Quill.Editor.WinUI.Localization;

namespace Quill.StatusBar;

/// <summary>
/// The word count at the bottom right (M6.5, Typora): "N words", or "selected / total" while text is
/// selected; clicking it opens a flyout with the reading time, lines, words and characters.
/// </summary>
internal sealed partial class WordCountButton : UserControl
{
    private readonly TextBlock _summary = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _minutes = Value();
    private readonly TextBlock _lines = Value();
    private readonly TextBlock _words = Value();
    private readonly TextBlock _characters = Value();
    private readonly TextBlock _titleBlock = new() { Opacity = 0.7, Margin = new Thickness(0, 0, 0, 6) };
    private readonly (TextBlock Block, string Key)[] _unitLabels;
    private TextCounts _total;
    private TextCounts? _selected;
    private int _wordsPerMinute = WordStatistics.DefaultWordsPerMinute;

    public WordCountButton()
    {
        var details = new Grid { ColumnSpacing = 12, RowSpacing = 6, MinWidth = 160 };
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumnSpan(_titleBlock, 2);
        details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        details.Children.Add(_titleBlock);

        var rows = new (TextBlock Value, string Key)[]
        {
            (_minutes, "status.minutes"),
            (_lines, "status.lines"),
            (_words, "status.words"),
            (_characters, "status.characters"),
        };
        _unitLabels = new (TextBlock Block, string Key)[rows.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            int row = details.RowDefinitions.Count;
            details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var label = new TextBlock();
            _unitLabels[i] = (label, rows[i].Key);
            Grid.SetRow(rows[i].Value, row);
            Grid.SetRow(label, row);
            Grid.SetColumn(label, 1);
            details.Children.Add(rows[i].Value);
            details.Children.Add(label);
        }

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(_summary);
        content.Children.Add(new FontIcon { Glyph = "\uE70E", FontSize = 10 });
        Button = new Button
        {
            Content = content,
            Padding = new Thickness(10, 2, 10, 2),
            Background = null,
            BorderThickness = new Thickness(0),
            Flyout = new Flyout { Content = details, Placement = FlyoutPlacementMode.TopEdgeAlignedRight },
        };
        Content = Button;

        ApplyLanguage();
        Loaded += (_, _) =>
        {
            UiStrings.LanguageChanged -= OnLanguageChanged;
            UiStrings.LanguageChanged += OnLanguageChanged;
            ApplyLanguage();
        };
        Unloaded += (_, _) => UiStrings.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyLanguage();

    private void ApplyLanguage()
    {
        _titleBlock.Text = UiStrings.Get("status.wordCountTitle");
        foreach (var (block, key) in _unitLabels)
        {
            block.Text = UiStrings.Get(key);
        }

        Show(_total, _selected);
    }

    public Button Button { get; }

    /// <summary>The summary as shown, for tests.</summary>
    public string Summary => _summary.Text;

    /// <summary>The reading time as shown, for tests.</summary>
    public string ReadingMinutes => _minutes.Text;

    /// <summary>The reading speed for the reading time (the setting, Q105).</summary>
    public int WordsPerMinute
    {
        get => _wordsPerMinute;
        set
        {
            if (_wordsPerMinute != value)
            {
                _wordsPerMinute = value;
                Show(_total, _selected);
            }
        }
    }

    /// <summary>Shows the counts of the document and, while text is selected, of the selection.</summary>
    public void Show(TextCounts total, TextCounts? selected)
    {
        _total = total;
        _selected = selected;
        _summary.Text = selected is { } part ? $"{part.Words} / {Words(total.Words)}" : Words(total.Words);
        var shown = selected ?? total;
        _minutes.Text = shown.MinutesAt(_wordsPerMinute).ToString();
        _lines.Text = shown.Lines.ToString();
        _words.Text = shown.Words.ToString();
        _characters.Text = shown.Characters.ToString();
    }

    private static string Words(int count) =>
        count == 1 ? UiStrings.Get("status.singleWord") : UiStrings.Format("status.manyWords", count);

    private static TextBlock Value() => new() { HorizontalAlignment = HorizontalAlignment.Right };
}
