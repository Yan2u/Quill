using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quill.Editor.Documents;
using Quill.Editor.Markdown;
using Quill.Editor.State;
using Quill.Editor.WinUI;
using Quill.Editor.WinUI.Theming;
using Quill.Playground.Samples;
using Windows.Storage.Pickers;

namespace Quill.Playground.Demos;

/// <summary>
/// The Editor page: a document in the <see cref="MarkdownBox"/> (keyboard, pointer and clipboard,
/// M5.3; the input method comes with M5.4). Choose the showcase, the GFM specification or any
/// Markdown file, and switch themes to see that the tree is not re-projected.
/// </summary>
internal sealed partial class EditorView : UserControl
{
    private readonly MarkdownBox _editor = new();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private string? _tracePath;
    private readonly TextBlock _status = new() { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly ComboBox _theme = new() { ItemsSource = new[] { "System theme", "Light", "Dark", "High contrast (system colors)" }, SelectedIndex = 0, MinWidth = 160 };
    private string _loaded = string.Empty;

    public EditorView()
    {
        // The text service events of each session go to .agents/temp/ime-editor for diagnosis (M5.4).
        _editor.TextInputTrace = line =>
        {
            if (_tracePath is not null)
            {
                File.AppendAllText(_tracePath, $"{_clock.Elapsed.TotalMilliseconds,10:F1}  {line}{Environment.NewLine}");
            }
        };
        var documents = new ComboBox { ItemsSource = new[] { "Showcase", "GFM specification" }, SelectedIndex = 0, MinWidth = 160 };
        documents.SelectionChanged += async (_, _) =>
        {
            _editor.DocumentDirectory = documents.SelectedIndex == 0 ? await ShowcaseFolderAsync() : null;
            Load(documents.SelectedIndex == 0 ? MarkdownShowcase.Text : ReadGfmSpec(), documents.SelectedItem as string);
        };
        var open = new Button { Content = "Open file..." };
        open.Click += async (_, _) => await OpenAsync();
        _theme.SelectionChanged += (_, _) => ApplyTheme();
        _editor.LinkActivated += async (_, link) =>
        {
            _status.Text = $"Ctrl+click: {link.Destination}";
            if (Uri.TryCreate(link.Destination, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto")
            {
                await Windows.System.Launcher.LaunchUriAsync(uri);
            }
        };

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(16, 4, 16, 8) };
        bar.Children.Add(documents);
        bar.Children.Add(open);
        bar.Children.Add(_theme);
        bar.Children.Add(_status);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(_editor, 1);
        layout.Children.Add(bar);
        layout.Children.Add(_editor);

        // The find panel floats over the editor, a sibling of it so its keys stay its own (12-find-replace.md).
        var find = new Quill.Editor.WinUI.Search.FindPanel(_editor);
        Grid.SetRow(find, 1);
        layout.Children.Add(find);
        Content = layout;

        Loaded += async (_, _) =>
        {
            if (!_editor.HasDocument)
            {
                _editor.DocumentDirectory = await ShowcaseFolderAsync();
                Load(MarkdownShowcase.Text, "Showcase");
            }
        };
    }

    private void Load(string markdown, string? name)
    {
        var watch = Stopwatch.StartNew();
        var ids = new SequentialBlockIdSource();
        var document = MarkdownImporter.Import(markdown, ids).Document;
        var state = new EditorState { Document = document, Selection = TextSelection.Caret(EditorDocuments.FirstCaret(document)) };
        double import = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        _tracePath = Path.Combine(RepositoryPaths.Temp("ime-editor"), $"ime-trace-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        _editor.Load(state, ids);
        ApplyTheme();
        _editor.Focus(FocusState.Programmatic);
        var report = _editor.Root.LastReport;
        string diagnostics = report?.Diagnostics.Count > 0 ? $", {report.Diagnostics.Count} diagnostics: {report.Diagnostics[0]}" : string.Empty;
        _loaded = $"{name}: {document.Children.Count} top-level blocks; import {import:F0} ms, projection, mount and layout {watch.Elapsed.TotalMilliseconds:F0} ms{diagnostics}";
        _status.Text = _loaded;
    }

    private void ApplyTheme()
    {
        if (_editor.Document is not { } document)
        {
            return;
        }

        document.StyleSheet = _theme.SelectedIndex switch
        {
            1 => MdStyleSheet.Light,
            2 => MdStyleSheet.Dark,
            3 => MdStyleSheet.HighContrast(),
            _ => null,
        };
    }

    private async Task OpenAsync()
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".md");
        picker.FileTypeFilter.Add(".markdown");
        picker.FileTypeFilter.Add(".txt");
        var window = XamlRoot?.ContentIslandEnvironment?.AppWindowId is { } id ? Microsoft.UI.Win32Interop.GetWindowFromWindowId(id) : 0;
        WinRT.Interop.InitializeWithWindow.Initialize(picker, window);
        if (await picker.PickSingleFileAsync() is { } file)
        {
            _editor.DocumentDirectory = Path.GetDirectoryName(file.Path);
            Load(await File.ReadAllTextAsync(file.Path), file.Name);
        }
    }

    /// <summary>A folder holding the showcase's <c>images/sample.png</c>, drawn on first use.</summary>
    private static async Task<string> ShowcaseFolderAsync()
    {
        string folder = RepositoryPaths.Temp("showcase");
        string image = Path.Combine(folder, "images", "sample.png");
        if (!File.Exists(image))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(image)!);
            using var target = new Microsoft.Graphics.Canvas.CanvasRenderTarget(Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice(), 320, 160, 96);
            using (var session = target.CreateDrawingSession())
            {
                session.Clear(Microsoft.UI.Colors.SteelBlue);
                session.FillEllipse(160, 80, 70, 40, Microsoft.UI.Colors.Orange);
            }

            await target.SaveAsync(image);
        }

        return folder;
    }

    private static string ReadGfmSpec() =>
        SpecCorpora.GfmSpecPath() is { } path ? File.ReadAllText(path) : "The GFM specification was not found under testdata.";
}
