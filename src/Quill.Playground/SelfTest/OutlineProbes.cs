using System.Text;
using Microsoft.UI.Xaml;
using Quill.Editor.Documents;
using Quill.Editor.Markdown;
using Quill.Editor.State;
using Quill.Editor.Statistics;
using Quill.Editor.WinUI;
using Quill.Editor.WinUI.Input;
using Quill.Playground.Samples;

namespace Quill.Playground.SelfTest;

/// <summary>
/// M6.5, layer 2: going to a heading from the outline puts it at the top of the view with the caret at
/// its start, and the first block in view gives that heading as the current one (the outline follows
/// the scroll position, Typora).
/// </summary>
internal static class OutlineProbes
{
    public static async Task RunAsync(SelfTestContext context)
    {
        context.Stage.Children.Clear();
        var box = new MarkdownBox
        {
            Width = 560,
            Height = 400,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Clipboard = new MemoryClipboard(),
        };
        context.Stage.Children.Add(box);
        await Task.Delay(50);

        var markdown = new StringBuilder();
        for (int section = 1; section <= 12; section++)
        {
            markdown.Append($"## Section {section}\n\n");
            for (int line = 0; line < 6; line++)
            {
                markdown.Append($"Paragraph {line} of section {section} with enough words to fill a line.\n\n");
            }
        }

        var ids = new SequentialBlockIdSource();
        var document = MarkdownImporter.Import(markdown.ToString(), ids).Document;
        box.Load(new EditorState { Document = document, Selection = TextSelection.Caret(EditorDocuments.FirstCaret(document)) }, ids);
        await Task.Delay(80);

        var outline = DocumentOutline.Of(box.State.Document);
        var target = outline[8];
        box.GoToHeading(target.Id);
        await Task.Delay(120);

        var top = box.Layout.GetBlockBounds(target.Id);
        var viewport = box.Layout.GetViewport();
        bool atTop = top is { } bounds && Math.Abs(bounds.Y - viewport.Y - 16) < 4;
        context.Check(atTop && box.State.Selection.Head is TextPosition { Offset: 0 } head && head.Block == target.Id,
            "outline: going to a heading puts it at the top of the view, the caret at its start", $"{top?.Y:F0} vs viewport {viewport.Y:F0}");

        var first = box.FirstVisibleBlock();
        var current = first is { } block ? DocumentOutline.CurrentHeading(box.State.Document, outline, block) : null;
        context.Check(current == target.Id, "outline: the first block in view gives the current heading", $"{current} vs {target.Id} ({target.Text})");

        box.Document!.ScrollViewer.ChangeView(null, viewport.Y + 260, null, disableAnimation: true);
        await Task.Delay(120);
        first = box.FirstVisibleBlock();
        current = first is { } later ? DocumentOutline.CurrentHeading(box.State.Document, outline, later) : null;
        context.Check(current is { } moved && outline.ToList().FindIndex(entry => entry.Id == moved) >= 8, "outline: scrolling down moves the current heading forward", $"{outline.FirstOrDefault(entry => entry.Id == current)?.Text}");
        context.Stage.Children.Clear();
    }
}
