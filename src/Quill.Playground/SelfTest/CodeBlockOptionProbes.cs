using Microsoft.UI.Xaml;
using Quill.Editor;
using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Markdown;
using Quill.Editor.State;
using Quill.Editor.WinUI;
using Quill.Editor.WinUI.Elements;
using Quill.Editor.WinUI.Input;
using Quill.Playground.Samples;

namespace Quill.Playground.SelfTest;

/// <summary>
/// M7.2, layer 2: the settings that change how code blocks show (10, 14-settings.md): line numbers in a
/// gutter that is not text (a click in it lands at the start of its line; front matter has none), math
/// code blocks shown as formulas like Mermaid blocks (only the formula unfocused, the code and the preview
/// focused, a message for a formula that does not parse), and the editor's options reaching the session.
/// </summary>
internal static class CodeBlockOptionProbes
{
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

        await CheckLineNumbersAsync(context, box);
        await CheckMathAsync(context, box);
        CheckOptions(context, box);
        box.ViewOptions = MdViewOptions.Default;
        box.Options = EditorOptions.Default;
        context.Stage.Children.Clear();
    }

    private static async Task CheckLineNumbersAsync(SelfTestContext context, MarkdownBox box)
    {
        Load(box, "---\ntitle: x\n---\n\nfirst\n\n```cs\nint a = 1;\nint b = 2;\nint c = 3;\n```");
        await Task.Delay(80);
        var code = Blocks<MdCodeBlock>(box).FirstOrDefault(static block => block is not MdFrontMatter);
        double plainLeft = code?.TextPadding.Left ?? 0;
        context.Check(code is { ShowsLineNumbers: false }, "code options: by default code blocks have no line numbers");

        box.ViewOptions = new MdViewOptions { CodeLineNumbers = true };
        await Task.Delay(80);
        code = Blocks<MdCodeBlock>(box).FirstOrDefault(static block => block is not MdFrontMatter);
        var front = Blocks<MdFrontMatter>(box).FirstOrDefault();
        context.Check(code is { ShowsLineNumbers: true } && code.TextPadding.Left > plainLeft + 8,
            "code options: line numbers take a gutter at the left of the code", $"text at {code?.TextPadding.Left:F0}, was {plainLeft:F0}");
        context.Check(front is { ShowsLineNumbers: false }, "code options: front matter has no line numbers");

        // Each number sits on the baseline of its own line (user report: all were drawn on the first line).
        var lines = ((CodeFence)box.State.Document.Children[^1]).Code;
        var expected = new List<double>();
        for (int start = 0, number = 1; number <= 3; number++)
        {
            var rect = box.Layout.GetCaretRect(new TextPosition(lines.Id, start));
            expected.Add(rect.Y + rect.Height / 2);
            start = lines.Text.IndexOf('\n', start) + 1;
        }

        var drawn = code?.DrawnLineNumbers.OrderBy(static pair => pair.Key).ToList() ?? [];
        var blockTop = box.Layout.GetBlockBounds(lines.Id)?.Y ?? 0;
        bool onTheirLines = drawn.Count == 3 && drawn.Select(static pair => pair.Key).SequenceEqual([1, 2, 3])
            && drawn.Zip(expected).All(pair => Math.Abs(blockTop + pair.First.Value - pair.Second) < 12)
            && drawn[1].Value - drawn[0].Value > 8 && drawn[2].Value - drawn[1].Value > 8;
        context.Check(onTheirLines, "code options: each line number is drawn on its own line",
            string.Join(", ", drawn.Select(static pair => $"{pair.Key}@{pair.Value:F0}")) + " | lines at " + string.Join(", ", expected.Select(y => $"{y - blockTop:F0}")));

        // A click in the gutter lands at the start of its line: the gutter is padding, not text.
        var leaf = ((CodeFence)box.State.Document.Children[^1]).Code;
        int second = leaf.Text.IndexOf('\n') + 1;
        var caret = box.Layout.GetCaretRect(new TextPosition(leaf.Id, second));
        var bounds = box.Layout.GetBlockBounds(leaf.Id);
        var hit = bounds is { } b ? box.Layout.HitTest(new Quill.Editor.Services.LayoutPoint(b.X + 4, caret.Y + caret.Height / 2)) : null;
        context.Check(hit is TextPosition { Offset: var offset } && offset == second, "code options: a click in the gutter puts the caret at the start of the line", $"{hit}");
    }

    private static async Task CheckMathAsync(SelfTestContext context, MarkdownBox box)
    {
        const string markdown = "first\n\n```math\nx^2 + y^2\n```\n\n```math\n\\frac{a\n```";
        box.ViewOptions = MdViewOptions.Default;
        Load(box, markdown);
        await Task.Delay(80);
        context.Check(Blocks<MdCodeBlock>(box).All(static block => block.Formula is null && !block.ShowsDiagramOnly), "code options: by default math code blocks show as code");

        box.ViewOptions = new MdViewOptions { CodeBlockMath = true };
        await Task.Delay(80);
        var blocks = Blocks<MdCodeBlock>(box);
        var math = blocks.ElementAtOrDefault(0);
        var broken = blocks.ElementAtOrDefault(1);
        double unfocused = math?.ActualHeight ?? 0;
        context.Check(math is { Formula: not null, ShowsDiagramOnly: true }, "code options: an unfocused math code block shows only its formula");
        context.Check(broken is { Formula: null, ShowsDiagramOnly: false, DiagramError.Length: > 0 }, "code options: a formula that does not parse shows as code with a message", broken?.DiagramError);

        var leaf = ((CodeFence)box.State.Document.Children[1]).Code;
        box.Execute(new SetSelection(TextSelection.Caret(new TextPosition(leaf.Id, 1))));
        await Task.Delay(80);
        math = Blocks<MdCodeBlock>(box).ElementAtOrDefault(0);
        context.Check(math is { IsFocused: true, ShowsDiagramOnly: false, Formula: not null } && math.ActualHeight > unfocused + 10,
            "code options: a focused math code block shows its code with the formula below", $"{math?.ActualHeight:F0} vs {unfocused:F0}");
    }

    private static void CheckOptions(SelfTestContext context, MarkdownBox box)
    {
        Load(box, "a ");
        box.Execute(new SetSelection(TextSelection.Caret(new TextPosition(box.State.Document.Children[0].Id, 2))));
        box.Execute(new InsertText("("));
        string paired = ((Paragraph)box.State.Document.Children[0]).Text;
        box.Options = EditorOptions.Default with { MatchBrackets = false };
        box.Execute(new InsertText("["));
        string plain = ((Paragraph)box.State.Document.Children[0]).Text;
        context.Check(paired == "a ()" && plain == "a ([)", "code options: the editor's options reach the session as they change", $"{paired} | {plain}");
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
