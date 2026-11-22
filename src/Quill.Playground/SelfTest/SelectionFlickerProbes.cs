using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Graphics.Canvas;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Markdown;
using Quill.Editor.State;
using Quill.Editor.WinUI;
using Quill.Editor.WinUI.Elements;
using Quill.Editor.WinUI.Text;
using Quill.Editor.WinUI.Theming;
using Quill.Playground.Samples;

namespace Quill.Playground.SelfTest;

/// <summary>
/// Reproduces and verifies pixel stability of unselected Chinese glyphs while dragging a selection
/// across subsequent text (or formatting) within the same paragraph or list item at fractional DIP
/// coordinates and high-DPI scale.
/// </summary>
internal static class SelectionFlickerProbes
{
    private const string ProbePhrase = "合并为";

    public static async Task RunAsync(SelfTestContext context)
    {
        context.Stage.Children.Clear();
        var box = new MarkdownBox
        {
            Width = 760,
            Height = 540,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        context.Stage.Children.Add(box);
        await Task.Delay(60);

        foreach (string font in new[] { "Segoe UI Variable Display", "Inter" })
        {
            var sheet = MdStyleSheet.Light with { FontFamily = font };
            await CheckScenarioAsync(context, box, sheet, font, "single-paragraph",
                "将多个连续的空白字符合并为一个空格，并测试中文选区拖拽时的闪烁问题。",
                targetBlockIndex: 0, scrollOffset: 0, pointerDown: true);

            await CheckScenarioAsync(context, box, sheet, font, "fractional-y-multi-block-frozen",
                "# 标题一\n\n第零段 `inline` 与 **粗体** 文字。\n\n## 次级标题\n\n将多个连续的空白字符合并为一个空格，并测试 `code` 和 **加粗** 在中文选区拖拽时的闪烁问题。",
                targetBlockIndex: 3, scrollOffset: 0, pointerDown: true);

            await CheckScenarioAsync(context, box, sheet, font, "fractional-y-multi-block-unfrozen",
                "# 标题一\n\n第零段 `inline` 与 **粗体** 文字。\n\n## 次级标题\n\n将多个连续的空白字符合并为一个空格，并测试 `code` 和 **加粗** 在中文选区拖拽时的闪烁问题。",
                targetBlockIndex: 3, scrollOffset: 0, pointerDown: false);

            await CheckListItemScenarioAsync(context, box, sheet, font);

            await CheckScenarioAsync(context, box, sheet, font, "multiline-wrapped",
                "将多个连续的空白字符合并为一个空格，并且我们增加足够多的中文内容让这段话自动折行成四行或者更多行，从而验证在多行自动换行的中文段落里从第一行末尾或者第二行向后拖拽选区时前面保持未选中的文字不会发生上下半像素抖动或者闪烁。",
                targetBlockIndex: 0, scrollOffset: 0, pointerDown: true);

            await CheckScenarioAsync(context, box, sheet, font, "scrolled-fractional",
                "# 顶部标题\n\n前导段落一。\n\n前导段落二。\n\n将多个连续的空白字符合并为一个空格，并测试滚动偏移小数位置下中文选区拖拽时的闪烁问题。\n\n后续段落。",
                targetBlockIndex: 3, scrollOffset: 17.3, pointerDown: true);
        }

        CheckDirectRenderTargetEquality(context);
        context.Stage.Children.Clear();
    }

    private static async Task CheckListItemScenarioAsync(SelfTestContext context, MarkdownBox box, MdStyleSheet sheet, string font)
    {
        string markdown = "- 第一条列表项\n- 将多个连续的空白字符合并为一个空格，并测试列表中的中文选区拖拽时的闪烁问题。";
        Load(box, markdown, sheet);
        await Task.Delay(100);

        if (box.State.Document.Children[0] is ContainerBlock list
            && list.Children[1] is ContainerBlock item
            && item.Children[0] is LeafBlock targetBlock)
        {
            await RunDragChecksAsync(context, box, font, "list-item", targetBlock, pointerDown: true, scrollOffset: 0);
            return;
        }

        context.Check(false, $"selection-flicker ({font}, list-item): list leaf block found");
    }

    private static async Task CheckScenarioAsync(
        SelfTestContext context,
        MarkdownBox box,
        MdStyleSheet sheet,
        string font,
        string scenario,
        string markdown,
        int targetBlockIndex,
        double scrollOffset,
        bool pointerDown)
    {
        Load(box, markdown, sheet);
        await Task.Delay(100);
        if (box.State.Document.Children[targetBlockIndex] is LeafBlock targetBlock)
        {
            await RunDragChecksAsync(context, box, font, scenario, targetBlock, pointerDown, scrollOffset);
            return;
        }

        context.Check(false, $"selection-flicker ({font}, {scenario}): target leaf block found");
    }

    private static async Task RunDragChecksAsync(
        SelfTestContext context,
        MarkdownBox box,
        string font,
        string scenario,
        LeafBlock targetBlock,
        bool pointerDown,
        double scrollOffset)
    {
        if (scrollOffset > 0)
        {
            box.Document!.ScrollViewer.ChangeView(null, scrollOffset, null, disableAnimation: true);
            await Task.Delay(80);
        }

        var view = box.Document?.Geometry.Find(targetBlock.Id) as MdTextBlockView;
        if (view is null || view.TextLayout is not { } layout)
        {
            context.Check(false, $"selection-flicker ({font}, {scenario}): target view found");
            return;
        }

        string sourceText = targetBlock.Text;
        int phraseStart = sourceText.IndexOf(ProbePhrase, StringComparison.Ordinal);
        int phraseEnd = phraseStart + ProbePhrase.Length;
        int dragAnchor = Math.Min(sourceText.Length - 2, phraseEnd + 3);
        int maxEnd = sourceText.Length;

        if (phraseStart < 0 || dragAnchor + 1 >= maxEnd)
        {
            context.Check(false, $"selection-flicker ({font}, {scenario}): valid phrase indices", sourceText);
            return;
        }

        if (pointerDown)
        {
            box.Execute(new SetInteraction(box.State.Interaction with { IsPointerDown = true }));
        }

        int[] dragOffsets =
        [
            Math.Min(maxEnd, dragAnchor + 1),
            Math.Min(maxEnd, dragAnchor + 4),
            Math.Min(maxEnd, dragAnchor + 8),
            Math.Min(maxEnd, dragAnchor + 12),
            Math.Min(maxEnd, dragAnchor + 18),
            Math.Min(maxEnd, dragAnchor + 13),
            Math.Min(maxEnd, dragAnchor + 9),
            Math.Min(maxEnd, dragAnchor + 5),
            Math.Min(maxEnd, dragAnchor + 15),
            Math.Min(maxEnd, dragAnchor + 2),
        ];

        if (box.CurrentProjection?.Map[targetBlock.Id] is not { } map)
        {
            context.Check(false, $"selection-flicker ({font}, {scenario}): block map found");
            return;
        }

        int dispStart = map.ToDisplay(phraseStart, Affinity.Forward);
        int dispEnd = map.ToDisplay(phraseEnd, Affinity.Backward);
        var regions = layout.Regions(dispStart, dispEnd);
        if (regions.Count == 0)
        {
            context.Check(false, $"selection-flicker ({font}, {scenario}): phrase region non-empty");
            return;
        }

        var textOrigin = box.Document!.Geometry.TextOrigin(view);
        var viewOriginInBox = view.TransformToVisual(box).TransformPoint(default);
        double scale = box.XamlRoot.RasterizationScale;
        var phraseRegion = regions[0];

        int cropX = Math.Max(0, (int)Math.Floor((viewOriginInBox.X + (textOrigin.X - box.Document.Geometry.Bounds(view).X) + phraseRegion.X) * scale) - 2);
        int cropY = Math.Max(0, (int)Math.Floor((viewOriginInBox.Y + (textOrigin.Y - box.Document.Geometry.Bounds(view).Y) + phraseRegion.Y) * scale) - 2);
        int cropW = Math.Max(4, (int)Math.Ceiling(phraseRegion.Width * scale) + 4);
        int cropH = Math.Max(4, (int)Math.Ceiling(phraseRegion.Height * scale) + 4);

        byte[]? baselineFrame = null;
        int baseWidth = 0;
        int baseHeight = 0;
        double baseCentroidY = 0;
        int worstDiff = 0;
        int worstStep = -1;
        int changedPixels = 0;
        double maxDy = 0;
        bool hasSubpixelColorDiff = false;
        var stepDiffs = new List<string>();

        for (int step = 0; step < dragOffsets.Length; step++)
        {
            int head = dragOffsets[step];
            int drawsBefore = view.DrawCount;
            box.Execute(new SetSelection(new TextSelection(
                new TextPosition(targetBlock.Id, dragAnchor),
                new TextPosition(targetBlock.Id, head))));
            await WaitForDrawAsync(view, drawsBefore);

            var capture = new RenderTargetBitmap();
            await capture.RenderAsync(box);
            byte[] pixels = (await capture.GetPixelsAsync()).ToArray();
            int pw = capture.PixelWidth;
            int ph = capture.PixelHeight;

            if (step == 0)
            {
                baselineFrame = pixels;
                baseWidth = pw;
                baseHeight = ph;
                baseCentroidY = CentroidY(pixels, pw, ph, cropX, cropY, cropW, cropH);
                hasSubpixelColorDiff = HasClearTypeColorDiff(pixels, pw, ph, cropX, cropY, cropW, cropH);
            }
            else if (baselineFrame is not null && pw == baseWidth && ph == baseHeight)
            {
                var (maxChannelDiff, diffCount) = CompareCrop(baselineFrame, pixels, pw, ph, cropX, cropY, cropW, cropH);
                double dy = Math.Abs(CentroidY(pixels, pw, ph, cropX, cropY, cropW, cropH) - baseCentroidY);
                if (dy > maxDy)
                {
                    maxDy = dy;
                }

                stepDiffs.Add($"s{step}:{maxChannelDiff}/{diffCount}");
                if (maxChannelDiff > worstDiff)
                {
                    worstDiff = maxChannelDiff;
                    worstStep = step;
                    changedPixels = diffCount;
                }
            }
        }

        if (pointerDown)
        {
            box.Execute(new SetInteraction(box.State.Interaction with { IsPointerDown = false }));
        }

        context.Info($"selection-flicker [{font} / {scenario}]: scale={scale:F2} maxDiff={worstDiff} changedPx={changedPixels} maxDy={maxDy:F5} clearType={hasSubpixelColorDiff} (worstStep={worstStep}) steps=[{string.Join(" ", stepDiffs)}]");
        context.Check(worstDiff <= 1 && maxDy < 0.02 && hasSubpixelColorDiff,
            $"selection-flicker ({font}, {scenario}): unselected '{ProbePhrase}' has zero glyph shift across drag steps with ClearType",
            $"maxDiff={worstDiff} changedPx={changedPixels} maxDy={maxDy:F5} clearType={hasSubpixelColorDiff}");
    }

    private static void CheckDirectRenderTargetEquality(SelfTestContext context)
    {
        const string sample = "将多个连续的空白字符合并为一个空格，并测试中文选区拖拽时的闪烁问题。";
        var sheet = MdStyleSheet.Light with { FontFamily = "Segoe UI Variable Display" };
        var device = CanvasDevice.GetSharedDevice();
        var source = TextSource.Plain(sample);
        var format = new BlockTextFormat(sheet.FontFamily, (float)sheet.FontSize, sheet.Foreground);
        using var layout = new BlockTextLayout(device, source, format, sheet, 700f);

        int phraseStart = sample.IndexOf(ProbePhrase, StringComparison.Ordinal);
        int phraseEnd = phraseStart + ProbePhrase.Length;
        int anchor = phraseEnd + 3;
        var phraseRegion = layout.Regions(phraseStart, phraseEnd)[0];

        const float dpi = 168f;
        const float scale = dpi / 96f;
        int cx = Math.Max(0, (int)Math.Floor(phraseRegion.X * scale) - 2);
        int cy = Math.Max(0, (int)Math.Floor(phraseRegion.Y * scale) - 2);
        int cw = Math.Max(4, (int)Math.Ceiling(phraseRegion.Width * scale) + 4);
        int ch = Math.Max(4, (int)Math.Ceiling(phraseRegion.Height * scale) + 4);
        int pxW = (int)Math.Ceiling(700f * scale);
        int pxH = (int)Math.Ceiling(layout.Height * scale);

        byte[]? baseline = null;
        int worstDiff = 0;
        int worstPx = 0;
        for (int step = 1; step <= 6; step++)
        {
            using var target = new CanvasRenderTarget(device, pxW / scale, pxH / scale, dpi);
            using (var ds = target.CreateDrawingSession())
            {
                ds.Clear(Colors.White);
                ds.TextAntialiasing = Microsoft.Graphics.Canvas.Text.CanvasTextAntialiasing.ClearType;
                layout.Draw(ds, default, (anchor, Math.Min(sample.Length, anchor + step * 2)));
            }

            byte[] pixels = target.GetPixelBytes(cx, cy, Math.Min(cw, (int)target.SizeInPixels.Width - cx), Math.Min(ch, (int)target.SizeInPixels.Height - cy));
            if (baseline is null)
            {
                baseline = pixels;
            }
            else
            {
                for (int i = 0; i + 3 < pixels.Length; i += 4)
                {
                    int d = Math.Max(Math.Max(Math.Abs(baseline[i] - pixels[i]), Math.Abs(baseline[i + 1] - pixels[i + 1])), Math.Abs(baseline[i + 2] - pixels[i + 2]));
                    if (d > 0)
                    {
                        worstPx++;
                        if (d > worstDiff)
                        {
                            worstDiff = d;
                        }
                    }
                }
            }
        }

        context.Check(worstDiff == 0 && worstPx == 0,
            "selection-flicker (offscreen render-target): unselected Chinese glyph pixels are bit-identical across selection steps",
            $"maxDiff={worstDiff} changedPx={worstPx}");
    }

    private static double CentroidY(byte[] pixels, int width, int height, int x0, int y0, int w, int h)
    {
        int x1 = Math.Min(width, x0 + w);
        int y1 = Math.Min(height, y0 + h);
        double weighted = 0;
        double total = 0;
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                int i = (y * width + x) * 4;
                double luminance = (pixels[i] + pixels[i + 1] + pixels[i + 2]) / 3.0;
                double darkness = 255.0 - luminance;
                weighted += (y - y0) * darkness;
                total += darkness;
            }
        }

        return total > 0 ? weighted / total : 0;
    }

    private static bool HasClearTypeColorDiff(byte[] pixels, int width, int height, int x0, int y0, int w, int h)
    {
        int x1 = Math.Min(width, x0 + w);
        int y1 = Math.Min(height, y0 + h);
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                int i = (y * width + x) * 4;
                if (pixels[i] != pixels[i + 1] || pixels[i + 1] != pixels[i + 2])
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static (int MaxDiff, int ChangedPixels) CompareCrop(byte[] a, byte[] b, int width, int height, int x0, int y0, int w, int h)
    {
        int x1 = Math.Min(width, x0 + w);
        int y1 = Math.Min(height, y0 + h);
        int maxDiff = 0;
        int changed = 0;
        for (int y = y0; y < y1; y++)
        {
            for (int x = x0; x < x1; x++)
            {
                int i = (y * width + x) * 4;
                int d = Math.Max(Math.Max(Math.Abs(a[i] - b[i]), Math.Abs(a[i + 1] - b[i + 1])), Math.Abs(a[i + 2] - b[i + 2]));
                if (d > 0)
                {
                    changed++;
                    if (d > maxDiff)
                    {
                        maxDiff = d;
                    }
                }
            }
        }

        return (maxDiff, changed);
    }

    private static async Task WaitForDrawAsync(MdBlockView view, int previousCount)
    {
        var watch = Stopwatch.StartNew();
        while (view.DrawCount <= previousCount && watch.ElapsedMilliseconds < 1500)
        {
            await Task.Delay(15);
        }

        await Task.Delay(35);
    }

    private static void Load(MarkdownBox box, string markdown, MdStyleSheet sheet)
    {
        var ids = new SequentialBlockIdSource();
        var document = MarkdownImporter.Import(markdown, ids).Document;
        box.LightStyleSheet = sheet;
        box.DarkStyleSheet = sheet;
        box.Load(new EditorState { Document = document, Selection = TextSelection.Caret(EditorDocuments.FirstCaret(document)) }, ids);
        if (box.Document is not null)
        {
            box.Document.StyleSheet = sheet;
        }
    }
}
