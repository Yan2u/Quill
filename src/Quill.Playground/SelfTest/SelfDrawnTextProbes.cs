using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Quill.Playground.TextPrototype;
using Windows.Foundation;
using Windows.Graphics.DirectX;
using Microsoft.UI;
using Color = Windows.UI.Color;

namespace Quill.Playground.SelfTest;

/// <summary>
/// Q8: the self-drawn text prototype (Q62) on Win2D. Engine checks run on the shared device without
/// any XAML element; integration checks host the control in the measurement window; PNGs of the
/// rendering are written next to the report for a human look.
/// </summary>
internal static class SelfDrawnTextProbes
{
    private const float Width = 420;

    private static readonly ProtoObject MathBox = new(46, 20, ProtoObjectAlign.Baseline, Color.FromArgb(0xFF, 0xC8, 0xE6, 0xC9), Baseline: 15);
    private static readonly ProtoObject ImageBox = new(40, 40, ProtoObjectAlign.Center, Color.FromArgb(0xFF, 0x90, 0xCA, 0xF9));

    public static async Task RunAsync(SelfTestContext context)
    {
        var device = CanvasDevice.GetSharedDevice();
        ProbeEngine(context, device);
        ProbePerformance(context, device);
        await ProbeControlsAsync(context);
        await WritePicturesAsync(context, device);
    }

    /// <summary>A paragraph with every style the prototype draws.</summary>
    public static IReadOnlyList<ProtoRun> Sample() =>
    [
        new("Self-drawn "), new("bold", ProtoStyle.Strong), new(", "), new("italic", ProtoStyle.Emphasis), new(", "),
        new("`", ProtoStyle.Marker), new("code span", ProtoStyle.Code), new("`", ProtoStyle.Marker), new(", "),
        new("highlight", ProtoStyle.Highlight), new(", "), new("struck", ProtoStyle.Strikethrough), new(", x"),
        new("2", ProtoStyle.Superscript), new(" H"), new("2", ProtoStyle.Subscript), new("O, 中文混排与 emoji 😀👍🏽, math "),
        ProtoRun.Inline(MathBox), new(" and an image "), ProtoRun.Inline(ImageBox), new(" next to text, "),
        new("a link", ProtoStyle.Link), new(", and a tail long enough to wrap over the lines of the block."),
    ];

    private static void ProbeEngine(SelfTestContext context, CanvasDevice device)
    {
        using (var zero = new StyledTextLayout(device, [new("ab​cd")], TextTheme.Light, 1000))
        {
            double advance = zero.CaretRect(3, backward: false).X - zero.CaretRect(2, backward: false).X;
            context.Check(Math.Abs(advance) < 0.01, "engine: a zero-width space takes no width", $"advance {advance:F3}");
        }

        using (var tab = new StyledTextLayout(device, [new("a\tb")], TextTheme.Light, 1000))
        {
            double stop = tab.CaretRect(2, backward: false).X;
            context.Check(Math.Abs(stop - TextTheme.Light.IncrementalTabStop) < 0.5, "engine: a tab advances to the next tab stop", $"b at {stop:F2}");
        }

        using var layout = new StyledTextLayout(device, Sample(), TextTheme.Light, Width);
        context.Check(layout.Layout.LineCount >= 3, "engine: the paragraph wraps at the block width", $"{layout.Layout.LineCount} lines, height {layout.Size.Y:F1}");

        // Every caret position must come back from a hit test just right of the caret.
        var mismatches = new List<string>();
        int positions = 0;
        foreach (int offset in GraphemeOffsets(layout.Text).Where(offset => offset < layout.Text.Length))
        {
            positions++;
            var caret = layout.CaretRect(offset, backward: false);
            int hit = layout.HitTest(new Vector2((float)caret.X + 0.75f, (float)(caret.Y + caret.Height / 2)));
            if (hit != offset)
            {
                mismatches.Add($"{offset}->{hit}");
            }
        }

        context.Check(mismatches.Count == 0, "engine: hit testing returns every caret offset", $"{positions} positions, mismatches: {string.Join(", ", mismatches.Take(8))}");

        int han = layout.Text.IndexOf('中');
        double hanWidth = layout.Regions(han, han + 1).First().Width;
        context.Check(hanWidth > TextTheme.Light.FontSize * 0.8, "engine: CJK falls back to a font that has it", $"'中' {hanWidth:F1} wide");

        int script = layout.Text.IndexOf("x2", StringComparison.Ordinal) + 1;
        var normal = layout.CaretRect(script - 1, backward: false);
        var superscript = layout.CaretRect(script, backward: false);
        context.Check(superscript.Height < normal.Height && superscript.Y + superscript.Height < normal.Y + normal.Height - 4, "engine: the caret in a superscript is shorter and raised",
            $"normal y={normal.Y:F1} h={normal.Height:F1}; superscript y={superscript.Y:F1} h={superscript.Height:F1}");

        int image = layout.Text.IndexOf("image ", StringComparison.Ordinal) + "image ".Length;
        var box = layout.Regions(image, image + 1).First();
        var line = layout.Layout.LineMetrics;
        var glyph = layout.Regions(image - 2, image - 1).First();
        double baseline = LineBaseline(layout, image);
        double textMiddle = baseline - (layout.Ascent - (glyph.Height - layout.Ascent)) / 2;
        context.Check(Math.Abs(box.Y + box.Height / 2 - textMiddle) < 2, "engine: an image next to text is centered on the text",
            $"box middle {box.Y + box.Height / 2:F1}, text middle {textMiddle:F1}");

        using (var lone = new StyledTextLayout(device, [ProtoRun.Inline(ImageBox)], TextTheme.Light, Width))
        {
            var only = lone.Regions(0, 1).First();
            context.Check(Math.Abs(only.X - (Width - ImageBox.Width) / 2) < 1, "engine: an image alone in its paragraph is centered", $"x={only.X:F1}");
        }

        var regions = layout.Regions(5, layout.Text.Length - 5).ToList();
        context.Check(regions.Count == layout.Layout.LineCount, "engine: a selection over the lines gives one rectangle per line", $"{regions.Count} rectangles");
    }

    private static void ProbePerformance(SelfTestContext context, CanvasDevice device)
    {
        var runs = Sample();
        var clock = Stopwatch.StartNew();
        const int Layouts = 200;
        for (int i = 0; i < Layouts; i++)
        {
            using var layout = new StyledTextLayout(device, runs, TextTheme.Light, Width - (i % 7));
        }

        double build = clock.Elapsed.TotalMilliseconds / Layouts;
        using var probe = new StyledTextLayout(device, runs, TextTheme.Light, Width);
        clock.Restart();
        for (int i = 0; i < 2000; i++)
        {
            probe.HitTest(new Vector2(i % (int)Width, (i * 7) % (int)probe.Size.Y));
        }

        double hit = clock.Elapsed.TotalMilliseconds / 2000 * 1000;
        clock.Restart();
        for (int offset = 0; offset <= probe.Text.Length; offset++)
        {
            probe.CaretRect(offset, backward: false);
        }

        double caret = clock.Elapsed.TotalMilliseconds / (probe.Text.Length + 1) * 1000;
        context.Info($"performance: layout of a {probe.Text.Length}-character paragraph {build:F3} ms; hit test {hit:F1} us; caret rectangle {caret:F1} us");
        context.Check(build < 2, "performance: building a paragraph layout stays well under the 4 ms keystroke budget", $"{build:F3} ms");
    }

    private static async Task ProbeControlsAsync(SelfTestContext context)
    {
        var runs = Sample();
        string text = string.Concat(runs.Select(static run => run.Text));

        var drawn = await MeasureBlocksAsync(context, () => new StyledTextView { Runs = runs }, elements =>
        {
            var first = (StyledTextView)elements[0];
            context.Check(first.CurrentLayout is { } current && Math.Abs(first.DesiredSize.Height - current.Size.Y) < 0.5,
                "control: the control measures to its text layout", $"{first.DesiredSize.Width:F0}x{first.DesiredSize.Height:F1}");
        });
        var views = drawn.Elements.Cast<StyledTextView>().ToList();
        context.Check(views.Any(static view => view.DrawCount > 0), "control: CanvasControl draws in the off-screen window",
            $"{views.Count(static view => view.DrawCount > 0)} of {views.Count} drawn");
        context.Info($"control: 40 CanvasControl blocks: first layout {drawn.Layout:F1} ms, width change {drawn.Resize:F1} ms, private bytes +{drawn.Megabytes:F1} MB");

        var tiled = await MeasureBlocksAsync(context, () => new StyledTextView(virtualSurface: true) { Runs = runs });
        int tiledDrawn = tiled.Elements.Cast<StyledTextView>().Count(static view => view.DrawCount > 0);
        context.Check(tiledDrawn is > 0 and < 40, "control: CanvasVirtualControl draws only the blocks in view", $"{tiledDrawn} of 40 drew");
        context.Info($"control: 40 CanvasVirtualControl blocks: first layout {tiled.Layout:F1} ms, width change {tiled.Resize:F1} ms, private bytes +{tiled.Megabytes:F1} MB, {tiledDrawn} drew");

        var plain = await MeasureBlocksAsync(context, () => new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        context.Info($"control: 40 XAML TextBlocks: first layout {plain.Layout:F1} ms, width change {plain.Resize:F1} ms, private bytes +{plain.Megabytes:F1} MB");
    }

    /// <summary>40 blocks in a scroll viewer (a few in view), after drawing: layout time, resize time and private bytes.</summary>
    private static async Task<(List<FrameworkElement> Elements, double Layout, double Resize, double Megabytes)> MeasureBlocksAsync(
        SelfTestContext context, Func<FrameworkElement> create, Action<List<FrameworkElement>>? inspect = null)
    {
        context.Stage.Children.Clear();
        await Task.Delay(200);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long baseline = PrivateBytes();
        var panel = new StackPanel { Width = Width, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        context.Stage.Children.Add(new ScrollViewer { Content = panel });
        var elements = Enumerable.Range(0, 40).Select(_ => create()).ToList();
        var clock = Stopwatch.StartNew();
        foreach (var element in elements)
        {
            panel.Children.Add(element);
        }

        panel.UpdateLayout();
        double layout = clock.Elapsed.TotalMilliseconds;
        inspect?.Invoke(elements);
        await Task.Delay(600);
        double megabytes = (PrivateBytes() - baseline) / 1024.0 / 1024.0;
        clock.Restart();
        panel.Width = 300;
        panel.UpdateLayout();
        double resize = clock.Elapsed.TotalMilliseconds;
        await Task.Delay(300);
        context.Stage.Children.Clear();
        return (elements, layout, resize, megabytes);
    }

    /// <summary>Writes q8-features.png (every style, selection, composition, carets) and q8-compare.png (XAML vs Win2D).</summary>
    private static async Task WritePicturesAsync(SelfTestContext context, CanvasDevice device)
    {
        string folder = Path.GetDirectoryName(context.TracePath!)!;
        float dpi = 96 * (float)context.Stage.XamlRoot.RasterizationScale;

        using (var layout = new StyledTextLayout(device, Sample(), TextTheme.Light, Width))
        using (var lone = new StyledTextLayout(device, [ProtoRun.Inline(ImageBox)], TextTheme.Light, Width))
        using (var target = new CanvasRenderTarget(device, Width + 20, layout.Size.Y + lone.Size.Y + 40, dpi))
        {
            using (var session = target.CreateDrawingSession())
            {
                session.Clear(Colors.White);
                int selection = layout.Text.IndexOf("italic", StringComparison.Ordinal);
                int composition = layout.Text.IndexOf("中文", StringComparison.Ordinal);
                var origin = new Vector2(10, 10);
                layout.Draw(session, origin, (selection, selection + 6), (composition, composition + 4));
                int script = layout.Text.IndexOf("x2", StringComparison.Ordinal);
                foreach (int caret in new[] { script + 1, script + 2, layout.Text.IndexOf("tail", StringComparison.Ordinal) })
                {
                    var rect = layout.CaretRect(caret, backward: false);
                    session.FillRectangle(new Rect(rect.X + origin.X, rect.Y + origin.Y, 1.5, rect.Height), Colors.Red);
                }

                lone.Draw(session, new Vector2(10, layout.Size.Y + 30));
            }

            await target.SaveAsync(Path.Combine(folder, "q8-features.png"));
        }

        context.Info("pictures: q8-features.png (styles, selection on 'italic', composition under '中文', red carets incl. one in the superscript, a lone centered image)");

        // The same text drawn by XAML and by Win2D, for comparing the text quality.
        const string Text = "The quick brown fox — 中文混排的段落 — jumps over 1234567890 lazy dogs. 😀";
        var block = new RichTextBlock { Width = Width, FontFamily = new FontFamily("Segoe UI"), FontSize = 16 };
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run { Text = Text + " " });
        paragraph.Inlines.Add(new Run { Text = "Bold", FontWeight = FontWeights.Bold });
        paragraph.Inlines.Add(new Run { Text = " and " });
        paragraph.Inlines.Add(new Run { Text = "italic", FontStyle = Windows.UI.Text.FontStyle.Italic });
        paragraph.Inlines.Add(new Run { Text = "." });
        block.Blocks.Add(paragraph);
        var host = new Border { Background = new SolidColorBrush(Colors.White), Child = block, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        context.Stage.Children.Clear();
        context.Stage.Children.Add(host);
        host.UpdateLayout();
        await Task.Delay(200);
        var capture = new RenderTargetBitmap();
        await capture.RenderAsync(host);
        byte[] pixels = (await capture.GetPixelsAsync()).ToArray();
        context.Check(capture.PixelWidth > 0, "pictures: RenderTargetBitmap captures XAML in the off-screen window", $"{capture.PixelWidth}x{capture.PixelHeight} px");
        context.Stage.Children.Clear();

        IReadOnlyList<ProtoRun> runs = [new(Text + " "), new("Bold", ProtoStyle.Strong), new(" and "), new("italic", ProtoStyle.Emphasis), new(".")];
        using var win2d = new StyledTextLayout(device, runs, TextTheme.Light, Width);
        using var xaml = CanvasBitmap.CreateFromBytes(device, pixels, capture.PixelWidth, capture.PixelHeight, DirectXPixelFormat.B8G8R8A8UIntNormalized, dpi);
        float rowHeight = (float)Math.Max(xaml.Size.Height, win2d.Size.Y) + 30;
        using var compare = new CanvasRenderTarget(device, Width + 20, rowHeight * 3, dpi);
        using (var session = compare.CreateDrawingSession())
        {
            session.Clear(Colors.White);
            using var label = new CanvasTextFormat { FontSize = 11, FontFamily = "Segoe UI" };
            session.DrawText("XAML RichTextBlock", 10, 2, Colors.Gray, label);
            session.DrawImage(xaml, 10, 18);
            session.DrawText("Win2D, grayscale antialiasing (what a transparent CanvasControl gets)", 10, rowHeight + 2, Colors.Gray, label);
            session.TextAntialiasing = CanvasTextAntialiasing.Grayscale;
            win2d.Draw(session, new Vector2(10, rowHeight + 18));
            session.DrawText("Win2D, ClearType (needs an opaque background)", 10, rowHeight * 2 + 2, Colors.Gray, label);
            session.TextAntialiasing = CanvasTextAntialiasing.ClearType;
            win2d.Draw(session, new Vector2(10, rowHeight * 2 + 18));
        }

        await compare.SaveAsync(Path.Combine(folder, "q8-compare.png"));
        context.Info($"pictures: q8-compare.png (XAML vs Win2D at rasterization scale {dpi / 96:F2}) in {folder}");
    }

    private static double LineBaseline(StyledTextLayout layout, int character)
    {
        double top = 0;
        int index = 0;
        foreach (var line in layout.Layout.LineMetrics)
        {
            if (character < index + line.CharacterCount)
            {
                return top + line.Baseline;
            }

            top += line.Height;
            index += line.CharacterCount;
        }

        return top;
    }

    private static IEnumerable<int> GraphemeOffsets(string text)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            yield return enumerator.ElementIndex;
        }

        yield return text.Length;
    }

    private static long PrivateBytes()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return process.PrivateMemorySize64;
    }
}
