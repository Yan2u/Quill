using System.Text;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Quill.Editor.Documents;
using Quill.Editor.Markdown;
using Quill.Editor.State;
using Quill.Editor.WinUI;
using Quill.Editor.WinUI.Elements;
using Quill.Editor.WinUI.Input;
using Quill.Editor.WinUI.Rendering;
using Quill.Editor.WinUI.Text;
using Quill.Editor.WinUI.Theming;
using Quill.Highlighting;
using Quill.Playground.Samples;

namespace Quill.Playground.SelfTest;

/// <summary>
/// M6.3, layer 2: code blocks colored in their language (TextMateSharp, Light+ / Dark+ by the style
/// sheet). A block without a language or in high contrast stays plain; switching themes recolors without
/// re-projecting; a block realized again takes its colors from the cache; long blocks laid out in pieces
/// re-lay out only the pieces whose text or colors changed.
/// </summary>
internal static class CodeHighlightProbes
{
    public static async Task RunAsync(SelfTestContext context)
    {
        // The grammars load in the background (the code shows plain until then); these checks need them.
        await GrammarCatalog.WarmUpAsync(HighlightTheme.Light);
        await GrammarCatalog.WarmUpAsync(HighlightTheme.Dark);
        CheckPieces(context);
        await CheckBlocksAsync(context);
    }

    private static void CheckPieces(SelfTestContext context)
    {
        var device = CanvasDevice.GetSharedDevice();
        var sheet = MdStyleSheet.Light;
        var format = new BlockTextFormat(sheet.FontFamily, sheet.FontSize, sheet.Foreground, Monospace: true);
        var highlighting = CodeHighlighting.Create(HighlightTheme.Light, "cs")!;
        var text = new StringBuilder();
        for (int i = 0; i < 300; i++)
        {
            text.Append($"int value{i} = {i}; // note").Append(i < 299 ? "\n" : string.Empty);
        }

        string code = text.ToString();
        var source = highlighting.Build(code);
        context.Check(source.Spans.Length > 300, "code: a C# block is colored", $"{source.Spans.Length} spans");
        var pieces = PiecewiseTextLayout.Create(device, source, format, sheet, 600, previous: null);
        int all = pieces.PieceCount;

        string typed = code.Replace("int value150 = 150;", "int value150 = 1500;");
        pieces = PiecewiseTextLayout.Create(device, highlighting.Build(typed), format, sheet, 600, pieces);
        context.Check(pieces.LaidOutPieces == 1, "code: typing in a long block lays out one piece", $"{pieces.LaidOutPieces} of {all}");

        // An opened comment recolors every line after it: every piece from there lays out again.
        string commented = "/*" + typed;
        pieces = PiecewiseTextLayout.Create(device, highlighting.Build(commented), format, sheet, 600, pieces);
        context.Check(pieces.LaidOutPieces == all, "code: a change of colors lays out the pieces it recolors", $"{pieces.LaidOutPieces} of {all}");
        pieces.Dispose();

        long analyzed = CodeHighlighting.AnalyzedLines;
        CodeHighlighting.Create(HighlightTheme.Light, "cs")!.Build(code);
        context.Check(CodeHighlighting.AnalyzedLines == analyzed, "code: a block highlighted before comes from the cache", $"{CodeHighlighting.AnalyzedLines - analyzed} lines analyzed");
    }

    private static async Task CheckBlocksAsync(SelfTestContext context)
    {
        context.Stage.Children.Clear();
        var box = new MarkdownBox
        {
            Width = 560,
            Height = 420,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Clipboard = new MemoryClipboard(),
        };
        context.Stage.Children.Add(box);
        await Task.Delay(50);

        Load(box, "---\ntitle: Quill\n---\n\nfirst\n\n```python\ndef f(x):\n    return x + 1\n```\n\n```\nplain code\n```\n\n```nosuchlanguage\nother code\n```");
        await Task.Delay(80);
        context.Check(FindAll<MdFrontMatter>(box).FirstOrDefault() is { ColoredSpans: > 0 }, "code: front matter is colored as YAML");
        var blocks = FindAll<MdCodeBlock>(box).Where(static block => block is not MdFrontMatter).ToList();
        context.Check(blocks.Count == 3, "code: three code blocks are realized", $"{blocks.Count}");
        context.Check(blocks.ElementAtOrDefault(0) is { ColoredSpans: > 2 }, "code: a block with a language is colored", $"{blocks.ElementAtOrDefault(0)?.ColoredSpans} spans");
        context.Check(blocks.ElementAtOrDefault(1) is { ColoredSpans: 0 } && blocks.ElementAtOrDefault(2) is { ColoredSpans: 0 }, "code: a block without a language or grammar stays plain");

        var update = box.LastUpdate;
        box.Document!.StyleSheet = MdStyleSheet.Dark;
        await Task.Delay(50);
        var python = FindAll<MdCodeBlock>(box)[1];
        context.Check(python.ColoredSpans > 2 && ReferenceEquals(box.LastUpdate, update), "code: switching to dark recolors without projecting", $"{python.ColoredSpans} spans");

        box.Document.StyleSheet = MdStyleSheet.HighContrast();
        await Task.Delay(50);
        context.Check(FindAll<MdCodeBlock>(box).All(static block => block.ColoredSpans == 0), "code: high contrast draws code in the system foreground");
        box.Document.StyleSheet = null;
        context.Stage.Children.Clear();
    }

    private static void Load(MarkdownBox box, string markdown)
    {
        var ids = new SequentialBlockIdSource();
        var document = MarkdownImporter.Import(markdown, ids).Document;
        box.Load(new EditorState { Document = document, Selection = TextSelection.Caret(EditorDocuments.FirstCaret(document)) }, ids);
    }

    private static List<T> FindAll<T>(MarkdownBox box)
        where T : MdBlockView =>
        box.Document!.Geometry.Realized.Where(view => !box.Document.Geometry.IsDetached(view)).OfType<T>().ToList();
}
