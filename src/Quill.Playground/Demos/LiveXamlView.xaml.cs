using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quill.Playground.Samples;
using Quill.Playground.VariableXaml;

namespace Quill.Playground.Demos;

/// <summary>
/// Live XAML editor: markup on the left is applied to a VariableXamlRoot on the right on every
/// keystroke, through incremental parsing and patching.
/// </summary>
public sealed partial class LiveXamlView : UserControl
{
    private int _eventCount;

    public LiveXamlView()
    {
        InitializeComponent();
        Preview.Updated += OnPreviewUpdated;
        Preview.MarkupEvent += OnMarkupEvent;
        ApplyOptions();
        SampleBox.SelectedIndex = 0;
    }

    private void OnSampleChanged(object sender, SelectionChangedEventArgs e)
    {
        string? tag = (SampleBox.SelectedItem as ComboBoxItem)?.Tag as string;
        Editor.Text = tag switch
        {
            "rich" => SampleDocuments.RichText,
            "article" => SampleDocuments.Article(300),
            "virtual" => SampleDocuments.Article(2000, virtualize: true),
            _ => SampleDocuments.LiveEditorStarter,
        };

        // TextChanged is not raised for text assigned before the TextBox is loaded.
        Preview.Xaml = Editor.Text;
    }

    private void OnEditorTextChanged(object sender, TextChangedEventArgs e) => Preview.Xaml = Editor.Text;

    private void OnOptionChanged(object sender, RoutedEventArgs e) => ApplyOptions();

    private void ApplyOptions()
    {
        Preview.Strategy = IncrementalPatchBox.IsChecked == true ? UpdateStrategy.Incremental : UpdateStrategy.FullReload;
        Preview.UseIncrementalParsing = IncrementalParseBox.IsChecked == true;
        Preview.UseXamlReaderForNewContent = XamlReaderBox.IsChecked == true;
        Preview.MeasureLayout = MeasureLayoutBox.IsChecked == true;
    }

    private void OnPreviewUpdated(VariableXamlRoot sender, UpdateReport report)
    {
        StatusText.Text = ReportFormatter.Format(report, sender.GetVirtualizationSummary());
        if (report.Error is not null)
        {
            DiagnosticsBar.Severity = InfoBarSeverity.Error;
            DiagnosticsBar.Title = "Markup error (previous content kept)";
            DiagnosticsBar.Message = report.Error.Message;
            DiagnosticsBar.IsOpen = true;
        }
        else if (report.Diagnostics.Count > 0)
        {
            DiagnosticsBar.Severity = InfoBarSeverity.Warning;
            DiagnosticsBar.Title = report.Diagnostics.Count == 1 ? "1 diagnostic" : $"{report.Diagnostics.Count} diagnostics";
            DiagnosticsBar.Message = string.Join("  |  ", report.Diagnostics.Take(3));
            DiagnosticsBar.IsOpen = true;
        }
        else
        {
            DiagnosticsBar.IsOpen = false;
        }
    }

    private void OnMarkupEvent(VariableXamlRoot sender, MarkupEventArgs args)
    {
        _eventCount++;
        EventBar.Title = $"Markup event \"{args.HandlerName}\"";
        EventBar.Message = $"Raised by {args.Sender?.GetType().Name ?? "unknown"}; {_eventCount} event(s) so far.";
        EventBar.IsOpen = true;
    }
}
