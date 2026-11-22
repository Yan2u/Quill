using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Quill.Editor.Documents;
using Quill.Editor.Markdown;
using Quill.Editor.State;
using Quill.Editor.WinUI;
using Quill.Editor.WinUI.Elements;
using Quill.Editor.WinUI.Input;
using Quill.Editor.WinUI.Rendering.Formulas;
using Quill.Editor.WinUI.Theming;
using Quill.Playground.Samples;
using Windows.UI;

namespace Quill.Playground.SelfTest;

/// <summary>
/// M6.3, layer 2: formulas typeset by RaTeX and drawn with the KaTeX fonts. The engine and the font
/// files load from the output folder, an invalid formula keeps its source, layouts are cached, a
/// display math block shows the formula while not focused, inline math is typeset in the paragraph,
/// and pixels are drawn in the given foreground (themes need no new layout).
/// </summary>
internal static class FormulaProbes
{
    public static async Task RunAsync(SelfTestContext context)
    {
        CheckEngine(context);
        CheckPainting(context);
        await CheckBlocksAsync(context);
    }

    private static void CheckEngine(SelfTestContext context)
    {
        var layout = MathTypesetter.Typeset(@"\frac{a}{b} + \sqrt{x^2+y^2}", display: true);
        context.Check(layout is { Width: > 0, Items.Length: > 5 }, "formula: RaTeX lays out a display formula", layout is null ? "null" : $"{layout.Width:F2} em, {layout.Items.Length} items");
        context.Check(MathTypesetter.Typeset(@"\frac{a", display: true) is null, "formula: a formula that does not parse has no layout");

        int before = MathTypesetter.Layouts;
        MathTypesetter.Typeset(@"\frac{a}{b} + \sqrt{x^2+y^2}", display: true);
        context.Check(MathTypesetter.Layouts == before, "formula: a repeated formula comes from the cache", $"{MathTypesetter.Layouts - before} new layouts");

        var face = KatexFonts.Face("Main-Regular");
        int glyph = face?.GetGlyphIndices(['x'])[0] ?? 0;
        context.Check(glyph != 0, "formula: the KaTeX fonts load from the output folder", KatexFonts.Directory);
    }

    private static void CheckPainting(SelfTestContext context)
    {
        var device = CanvasDevice.GetSharedDevice();
        var layout = MathTypesetter.Typeset(@"\sum_{i=1}^{n} i^2 = \frac{n(n+1)(2n+1)}{6}", display: true)!;
        const float size = 24;
        var red = Color.FromArgb(255, 255, 0, 0);
        int width = (int)Math.Ceiling(layout.Width * size) + 4;
        int height = (int)Math.Ceiling(layout.TotalHeight * size) + 4;
        using var target = new CanvasRenderTarget(device, width, height, 96);
        using (var session = target.CreateDrawingSession())
        {
            session.Clear(Colors.Transparent);
            MathPainter.Draw(session, layout, new Vector2(2, 2), size, red, "Segoe UI");
        }

        var pixels = target.GetPixelColors();
        int inked = pixels.Count(pixel => pixel.A > 128);
        int reddish = pixels.Count(pixel => pixel.A > 128 && pixel.R > 200 && pixel.G < 60 && pixel.B < 60);
        context.Check(inked > width * height / 50, "formula: glyphs, rules and paths draw ink", $"{inked} of {pixels.Length} pixels");
        context.Check(reddish > inked / 2, "formula: the default color draws in the given foreground", $"{reddish} red of {inked}");

        // CJK in \text is not in the KaTeX fonts: drawn with the text font.
        var cjk = MathTypesetter.Typeset(@"\text{中文} x", display: false)!;
        int fallbacks = MathPainter.FallbackGlyphs;
        using (var session = target.CreateDrawingSession())
        {
            MathPainter.Draw(session, cjk, Vector2.Zero, size, red, "Microsoft YaHei UI");
        }

        context.Check(MathPainter.FallbackGlyphs - fallbacks == 2, "formula: characters missing from the math fonts fall back to the text font", $"{MathPainter.FallbackGlyphs - fallbacks} fallbacks");
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

        Load(box, "first\n\n$$\n\\int_0^1 x^2\\,dx = \\frac{1}{3}\n$$\n\n$$\n\\frac{a\n$$\n\ninline $e^{i\\pi}+1=0$ here");
        await Task.Delay(80);
        var blocks = FindAll<MdMathBlock>(box);
        var valid = blocks.ElementAtOrDefault(0);
        var invalid = blocks.ElementAtOrDefault(1);
        var formula = valid?.Formula;
        context.Check(formula is not null, "formula: an unfocused math block shows the typeset formula");
        if (valid is not null && formula is not null)
        {
            double expected = formula.TotalHeight * MdStyleSheet.Light.FontSize * MdStyleSheet.Light.MathScale;
            double inner = valid.ActualHeight - valid.TextPadding.Top - valid.TextPadding.Bottom;
            context.Check(Math.Abs(inner - expected) < 1.5, "formula: the block is as tall as the formula", $"{inner:F1} vs {expected:F1}");
        }

        context.Check(invalid is { Formula: null, TextLayout: not null }, "formula: a block that does not parse keeps its source");

        int before = MathTypesetter.Layouts;
        MathTypesetter.Typeset(@"e^{i\pi}+1=0", display: false);
        context.Check(MathTypesetter.Layouts == before, "formula: inline math is typeset in the paragraph", $"{MathTypesetter.Layouts - before} new layouts");

        // Focused, the block shows its source again (10).
        box.Execute(new Quill.Editor.Commands.SetSelection(TextSelection.Caret(new TextPosition(box.State.Document.Children[1].Id, 0))));
        await Task.Delay(50);
        valid = FindAll<MdMathBlock>(box).FirstOrDefault();
        context.Check(valid is { IsFocused: true, Formula: null }, "formula: a focused math block shows its source");
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
