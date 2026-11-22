using Microsoft.UI.Xaml;
using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Markdown;
using Quill.Editor.State;
using Quill.Editor.WinUI;
using Quill.Editor.WinUI.Elements;
using Quill.Editor.WinUI.Input;
using Quill.Editor.WinUI.Rendering.Diagrams;
using Quill.Editor.WinUI.Theming;
using Quill.Playground.Samples;

namespace Quill.Playground.SelfTest;

/// <summary>
/// M6.3, layer 2: the live preview of a focused math block and the flyout of active inline math (10);
/// content wider than its block scrolling horizontally with a native scroll bar (a wide formula; code
/// when code does not wrap, the caret's column scrolled into view); a Mermaid source with an error
/// showing its message; diagram renders debounced while typing.
/// </summary>
internal static class RenderPolishProbes
{
    private const string Wide = @"\sum_{i=1}^{n} a_i b_i c_i d_i e_i f_i g_i h_i + \int_0^1 x^2 \, dx + \frac{a+b+c+d+e+f}{g+h+i+j+k+l} + \sqrt{x_1^2 + x_2^2 + x_3^2 + x_4^2 + x_5^2 + x_6^2 + x_7^2}";

    public static async Task RunAsync(SelfTestContext context)
    {
        context.Stage.Children.Clear();
        var box = new MarkdownBox
        {
            Width = 560,
            Height = 700,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Clipboard = new MemoryClipboard(),
        };
        context.Stage.Children.Add(box);
        await Task.Delay(50);
        string folder = RepositoryPaths.Temp("m6-3");

        await CheckMathAsync(context, box, folder);
        await CheckCodeAsync(context, box, folder);
        await CheckMermaidAsync(context, box, folder);
        context.Info($"render polish pictures in {folder}");
        context.Stage.Children.Clear();
    }

    private static async Task CheckMathAsync(SelfTestContext context, MarkdownBox box, string folder)
    {
        Load(box, "first\n\n$$\n" + Wide + "\n$$\n\n$$\nx^2\n$$\n\ninline $e^{i\\pi}+1=0$ here");
        await Task.Delay(80);
        var wide = Blocks<MdMathBlock>(box).ElementAtOrDefault(0);
        context.Check(wide is { Formula: not null, IsOverflowing: true, HorizontalScrollBar.Visibility: Visibility.Visible }, "polish: a formula wider than the column shows a scroll bar");
        var narrow = Blocks<MdMathBlock>(box).ElementAtOrDefault(1);
        context.Check(narrow is { IsOverflowing: false } && narrow.HorizontalScrollBar is not { Visibility: Visibility.Visible }, "polish: a formula that fits has no scroll bar");
        if (wide is not null)
        {
            // A press on the bar is the bar's: the editor must not capture the pointer (the drag would end).
            var thumb = Descendants(wide.HorizontalScrollBar!).OfType<Microsoft.UI.Xaml.Controls.Primitives.Thumb>().FirstOrDefault(static thumb => thumb.Visibility == Visibility.Visible);
            context.Check(thumb is not null && box.IsInControl(thumb), "polish: a press on the scroll bar is left to the scroll bar");
            context.Check(!box.IsInControl(wide), "polish: a press on the text is the editor's");
            // What a thumb drag does: the bar's Value changes; the formula must follow and draw anew.
            await SaveAsync(box, folder, "polish-math-before-value.png");
            int draws = wide.DrawCount;
            wide.HorizontalScrollBar!.Value = 120;
            await Task.Delay(60);
            context.Check(Math.Abs(wide.HorizontalOffset - 120) < 0.5 && wide.DrawCount > draws, "polish: setting the bar's value scrolls and redraws the formula", $"offset {wide.HorizontalOffset:F0}, {wide.DrawCount - draws} draws");
            await SaveAsync(box, folder, "polish-math-after-value.png");
            wide.ScrollHorizontallyTo(0);
            wide.ScrollHorizontallyTo(80);
            context.Check(Math.Abs(wide.HorizontalOffset - 80) < 0.5 && Math.Abs(wide.HorizontalScrollBar!.Value - 80) < 0.5, "polish: scrolling moves the formula and the bar together");
            wide.ScrollHorizontallyTo(1e6);
            context.Check(wide.HorizontalOffset <= wide.HorizontalScrollBar.Maximum + 0.5, "polish: scrolling stops at the end of the formula");
            wide.ScrollHorizontallyTo(0);
        }

        await SaveAsync(box, folder, "polish-math-wide.png");

        // Focused: the source with the live preview below it.
        var id = box.State.Document.Children[2].Id;
        box.Execute(new SetSelection(TextSelection.Caret(new TextPosition(id, 3))));
        await Task.Delay(60);
        var focused = Blocks<MdMathBlock>(box).ElementAtOrDefault(1);
        double withPreview = focused?.ActualHeight ?? 0;
        double source = (focused?.TextLayout?.Height ?? 0) + (focused?.TextPadding.Top ?? 0) * 2;
        context.Check(focused is { IsFocused: true, Preview: not null } && withPreview > source + 10, "polish: a focused math block shows a live preview below its source", $"{withPreview:F0} vs source {source:F0}");
        await SaveAsync(box, folder, "polish-math-preview.png");

        box.Execute(new InsertText("\\frac{"));
        await Task.Delay(60);
        focused = Blocks<MdMathBlock>(box).ElementAtOrDefault(1);
        context.Check(focused is { Preview: not null }, "polish: the preview keeps the last formula while the source does not parse");

        // Active inline math: its preview flyout below the source.
        var paragraph = box.State.Document.Children[3].Id;
        box.Execute(new SetSelection(TextSelection.Caret(new TextPosition(paragraph, 9))));
        await Task.Delay(60);
        var preview = box.Document!.Adorners.MathPreview;
        context.Check(preview is { Source: @"e^{i\pi}+1=0" }, "polish: active inline math shows its preview flyout", preview?.Source);
        if (preview is { } shown && box.Layout.GetCaretRect(new TextPosition(paragraph, 9)) is var caret)
        {
            context.Check(shown.Bounds.Top >= caret.Bottom - 0.5, "polish: the preview flyout lies below the source", $"{shown.Bounds.Top:F0} vs caret bottom {caret.Bottom:F0}");
        }

        await SaveAsync(box, folder, "polish-inline-preview.png");
        box.Execute(new SetSelection(TextSelection.Caret(new TextPosition(box.State.Document.Children[0].Id, 0))));
        await Task.Delay(40);
        context.Check(box.Document.Adorners.MathPreview is null, "polish: the preview flyout closes when the formula is no longer active");
    }

    private static async Task CheckCodeAsync(SelfTestContext context, MarkdownBox box, string folder)
    {
        string longLine = "var message = \"" + new string('x', 160) + "\"; // the end";
        Load(box, "```cs\nint a = 1;\n" + longLine + "\n```");
        await Task.Delay(60);
        context.Check(Blocks<MdCodeBlock>(box).FirstOrDefault() is { IsOverflowing: false }, "polish: wrapped code does not scroll");

        // Wrapping is a setting (14-settings.md), not part of the theme: the view options change, the theme stays.
        var sheet = box.Document!.EffectiveStyleSheet;
        box.ViewOptions = new MdViewOptions { WrapCode = false };
        await Task.Delay(60);
        var code = Blocks<MdCodeBlock>(box).FirstOrDefault();
        context.Check(code is { IsOverflowing: true, HorizontalScrollBar.Visibility: Visibility.Visible } && ReferenceEquals(sheet, box.Document.EffectiveStyleSheet),
            "polish: code that does not wrap scrolls when a line overflows");

        if (code?.HorizontalScrollBar is { } codeBar)
        {
            codeBar.Value = 100;
            await Task.Delay(60);
            code = Blocks<MdCodeBlock>(box).FirstOrDefault();
            context.Check(code is { TextScrollOffset: > 99 and < 101 }, "polish: setting the bar's value scrolls the code", $"offset {code?.TextScrollOffset:F0}");
            code?.ScrollHorizontallyTo(0);
        }

        // The caret at the end of the long line: its column scrolls into view.
        var codeLeaf = ((CodeFence)box.State.Document.Children[0]).Code;
        var id = codeLeaf.Id;
        int end = codeLeaf.Text.Length;
        box.Execute(new SetSelection(TextSelection.Caret(new TextPosition(id, end))));
        await Task.Delay(60);
        code = Blocks<MdCodeBlock>(box).FirstOrDefault();
        var caret = box.Layout.GetCaretRect(new TextPosition(id, end));
        var bounds = box.Layout.GetBlockBounds(id);
        context.Check(code is { TextScrollOffset: > 0 } && bounds is { } b && caret.X <= b.X + b.Width && caret.X >= b.X, "polish: the caret's column scrolls into view", $"offset {code?.TextScrollOffset:F0}, caret {caret.X:F0}, block {bounds?.X:F0}+{bounds?.Width:F0}");
        var hit = box.Layout.HitTest(new Quill.Editor.Services.LayoutPoint(caret.X - 1, caret.Y + caret.Height / 2));
        context.Check(hit is TextPosition { Offset: var offset } && Math.Abs(offset - end) <= 1, "polish: hit testing follows the scrolled text", $"{hit}");
        await SaveAsync(box, folder, "polish-code-scrolled.png");
        box.ViewOptions = MdViewOptions.Default;
        await Task.Delay(60);
        context.Check(Blocks<MdCodeBlock>(box).FirstOrDefault() is { IsOverflowing: false }, "polish: turning wrapping back on wraps the code again");
    }

    private static async Task CheckMermaidAsync(SelfTestContext context, MarkdownBox box, string folder)
    {
        Load(box, "first\n\n```mermaid\nflowchart TD\n  A --> B\n```\n\n```mermaid\nnot a diagram\n```");
        var broken = await WaitForAsync(box, static block => block.DiagramError is not null);
        double plain = (broken?.TextLayout?.Height ?? 0) + (broken?.TextPadding.Top ?? 0) * 2;
        context.Check(broken is not null && broken.ActualHeight > plain + 8, "polish: a Mermaid source with an error shows its message below the source", broken?.DiagramError);

        // Typing fast: the renders of sources typed over are skipped.
        var diagram = await WaitForAsync(box, static block => block.Diagram is not null);
        var mermaid = ((CodeFence)box.State.Document.Children[1]).Code;
        var id = mermaid.Id;
        box.Execute(new SetSelection(TextSelection.Caret(new TextPosition(id, mermaid.Text.Length))));
        int skipped = MermaidDiagrams.Skipped;
        foreach (char c in "\n  B --> C")
        {
            box.Execute(new InsertText(c.ToString()));
            await Task.Delay(10);
        }

        await Task.Delay(MermaidDiagrams.Debounce * 3);
        await WaitForAsync(box, static block => block.Diagram is not null);
        context.Check(MermaidDiagrams.Skipped - skipped >= 5, "polish: diagram renders are debounced while typing", $"{MermaidDiagrams.Skipped - skipped} skipped of 9 keys");
        string final = ((CodeFence)box.State.Document.Children[1]).Code.Text;
        bool rendered = false;
        for (int i = 0; i < 50 && !rendered; i++)
        {
            rendered = box.Renders!.TryGet(MermaidDiagrams.LightKind, final, out var result) && result is MermaidDiagram;
            await Task.Delay(20);
        }

        context.Check(rendered, "polish: the last source typed is rendered", final.Replace("\n", " / "));

        // Selected while shown as a diagram.
        box.Execute(new SetSelection(new TextSelection(new TextPosition(box.State.Document.Children[0].Id, 0), new TextPosition(((CodeFence)box.State.Document.Children[2]).Code.Id, 3))));
        await Task.Delay(60);
        await SaveAsync(box, folder, "polish-mermaid-selected.png");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child))
            {
                yield return nested;
            }
        }
    }

    private static async Task SaveAsync(MarkdownBox box, string folder, string name)
    {
        await EditorRenderProbes.WaitForDrawsAsync(box);
        await EditorRenderProbes.SaveAsync(box, Path.Combine(folder, name));
    }

    private static async Task<MdCodeBlock?> WaitForAsync(MarkdownBox box, Func<MdCodeBlock, bool> condition)
    {
        for (int i = 0; i < 100; i++)
        {
            if (Blocks<MdCodeBlock>(box).FirstOrDefault(condition) is { } found)
            {
                return found;
            }

            await Task.Delay(20);
        }

        return null;
    }

    private static void Load(MarkdownBox box, string markdown)
    {
        var ids = new SequentialBlockIdSource();
        var document = MarkdownImporter.Import(markdown, ids).Document;
        box.Load(new EditorState { Document = document, Selection = TextSelection.Caret(EditorDocuments.FirstCaret(document)) }, ids);
    }

    private static List<T> Blocks<T>(MarkdownBox box)
        where T : MdBlockView =>
        box.Document!.Geometry.Realized.Where(view => !box.Document.Geometry.IsDetached(view)).OfType<T>().ToList();
}
