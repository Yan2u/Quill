using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Quill.Playground.ImePrototype;
using Quill.Playground.Samples;

namespace Quill.Playground.Demos;

/// <summary>
/// The IME lab (Q6): a custom input surface on CoreTextEditContext for a manual session with a
/// real input method. Every text service event and key goes to an on-screen log and to a file
/// under <c>.agents/temp/ime-lab</c>, so the session can be analyzed afterwards.
/// </summary>
internal sealed partial class ImeLabView : UserControl
{
    private static readonly string[] InitialBlocks =
    [
        "First block: type Chinese with Microsoft Pinyin here.",
        string.Empty,
        "Third block: 中文与 English 混排, emoji 👍🏽.",
    ];

    private const string Checklist =
        "Checklist (Microsoft Pinyin, about 5 minutes):\n" +
        "1. Click the first block and type a few Chinese words: the composition is underlined, the candidate window sits under the text.\n" +
        "2. Type a long pinyin string, press Backspace a few times during the composition, then pick a candidate.\n" +
        "3. Press Esc during a composition: the composition disappears and nothing is left behind.\n" +
        "4. Press Enter after committing text: a new block starts; type Chinese there.\n" +
        "5. Use Up/Down to switch blocks (including the empty second block) and type Chinese in each.\n" +
        "6. Type English, punctuation (，。“”) and \"->\" (becomes an arrow) in Chinese and English modes.\n" +
        "7. Press Ctrl+Z after a composition: the whole committed phrase is undone at once.\n" +
        "8. Click another window and back, then continue typing.";

    private const int VisibleLogLines = 200;

    private readonly TextBox _logView;
    private readonly Grid _surfaceHost = new();
    private ImeEventLog _log = null!;
    private ImeSurface? _surface;
    private bool _logRefreshQueued;

    public ImeLabView()
    {
        _logView = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            IsTabStop = false,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(_logView, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(_logView, ScrollBarVisibility.Auto);

        var reset = new Button { Content = "Reset document and log" };
        reset.Click += (_, _) => CreateSurface();
        var logPath = new TextBlock { IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap, Opacity = 0.7 };

        var left = new Grid { RowSpacing = 8, Margin = new Thickness(16, 8, 8, 16) };
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var header = new StackPanel { Spacing = 6 };
        header.Children.Add(new TextBlock { Text = "IME lab (Q6): CoreTextEditContext", FontWeight = FontWeights.SemiBold, FontSize = 18 });
        header.Children.Add(new TextBlock { Text = Checklist, TextWrapping = TextWrapping.Wrap });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        actions.Children.Add(reset);
        actions.Children.Add(logPath);
        Grid.SetRow(actions, 1);
        _surfaceHost.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Gray);
        _surfaceHost.BorderThickness = new Thickness(1);
        Grid.SetRow(_surfaceHost, 2);
        left.Children.Add(header);
        left.Children.Add(actions);
        left.Children.Add(_surfaceHost);

        var root = new Grid { ColumnSpacing = 8 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        Grid.SetColumn(_logView, 1);
        _logView.Margin = new Thickness(0, 8, 16, 16);
        root.Children.Add(left);
        root.Children.Add(_logView);
        Content = root;

        CreateSurface();
        logPath.Text = $"Log: {_log.FilePath}";
        reset.Click += (_, _) => logPath.Text = $"Log: {_log.FilePath}";
    }

    private void CreateSurface()
    {
        _surfaceHost.Children.Clear();
        _logView.Text = string.Empty;
        string directory = RepositoryPaths.Temp("ime-lab");
        _log = new ImeEventLog(Path.Combine(directory, $"ime-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt"));
        var log = _log;
        _log.LineAdded += _ =>
        {
            // Refresh later, outside any text service callback, and only the tail, so a long session
            // does not slow down the surface; the file has everything.
            if (_logRefreshQueued)
            {
                return;
            }

            _logRefreshQueued = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                _logRefreshQueued = false;
                _logView.Text = string.Join("\n", log.Lines.Skip(Math.Max(0, log.Lines.Count - VisibleLogLines)));
                _logView.SelectionStart = _logView.Text.Length;
            });
        };
        _surface = new ImeSurface(new ImeTextStore(InitialBlocks), _log);
        _surfaceHost.Children.Add(_surface);
    }
}
