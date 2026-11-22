using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Quill.Editor.Documents;
using Quill.Editor.Markdown;
using Quill.Editor.Projection;
using Quill.Editor.State;
using Quill.Editor.WinUI;
using Quill.Editor.WinUI.Elements;
using Quill.Editor.WinUI.Theming;
using Quill.Playground.Samples;
using Quill.VariableXaml;
using Quill.VariableXaml.Model;

namespace Quill.Playground.SelfTest;

/// <summary>
/// M5.1: read-only rendering through the real element controls. Every CommonMark and GFM example is
/// imported, projected and mounted: no diagnostics, no placeholders, no XamlReader, and every block
/// of the VNode realized as its <c>Md*</c> control. Then the GFM specification as one long document
/// in the virtual stack, and a theme switch that relays out and redraws without re-projecting.
/// </summary>
internal static class EditorRenderProbes
{
    public static async Task RunAsync(SelfTestContext context)
    {
        context.Stage.Children.Clear();
        var root = new VariableXamlRoot { DeferUpdates = false, MeasureLayout = true, Width = 900, Height = 700, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        MarkdownElements.Configure(root);
        context.Stage.Children.Add(root);
        await Task.Delay(50);

        RenderCorpus(context, root, "CommonMark", SpecCorpora.CommonMark());
        RenderCorpus(context, root, "GFM", SpecCorpora.Gfm());
        await RenderLongDocumentAsync(context, root);
        await CaptureShowcaseAsync(context, root);
        await VerifyTableSingleCanvasAndUnloadAsync(context);
        await VerifyTableResizeAndPartialInvalidateAsync(context);
        context.Stage.Children.Clear();
    }

    private static async Task VerifyTableSingleCanvasAndUnloadAsync(SelfTestContext context)
    {
        WeakReference<object> boxRef = await MountAndVerifyTableAsync(context);
        context.Stage.Children.Clear();
        await Task.Delay(50);
        CanvasDevice.GetSharedDevice().Trim();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        await Task.Delay(20);
        CanvasDevice.GetSharedDevice().Trim();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        context.Check(!boxRef.TryGetTarget(out _), "render: unloading MarkdownBox allows GC collection");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference<object>> MountAndVerifyTableAsync(SelfTestContext context)
    {
        context.Stage.Children.Clear();
        var box = new MarkdownBox
        {
            Width = 600,
            Height = 320,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsReadOnly = true,
        };
        context.Stage.Children.Add(box);

        var ids = new SequentialBlockIdSource();
        const string tableMarkdown = """
            | Col A | Col B | Col C |
            | :--- | :---: | ---: |
            | Alpha | Beta | Gamma |
            | One | Two | Three |
            """;
        Document document = MarkdownImporter.Import(tableMarkdown, ids).Document;
        box.Load(EditorState.AtStart(document), ids);
        box.UpdateLayout();
        await WaitForDrawsAsync(box);

        MdTable? table = FindDescendant<MdTable>(box);
        int tableCanvases = table is null ? -1 : FindAll<CanvasVirtualControl>(table).Count();
        List<MdTableCell> cells = table is null ? [] : FindAll<MdTableCell>(table).ToList();
        bool cellsOk = cells.Count == 9
            && cells.All(static cell => cell.DrawCount > 0 && cell.LastDrawError is null && FindAll<CanvasVirtualControl>(cell).Count() == 0);
        context.Check(
            table is { LastDrawError: null } && tableCanvases == 1 && cellsOk,
            "render: table uses one CanvasVirtualControl and draws all cells without error",
            $"table={table is not null}, canvases={tableCanvases}, cells={cells.Count}, drawnCells={cells.Count(static c => c.DrawCount > 0)}, tableError={table?.LastDrawError?.Message}");

        context.Stage.Children.Clear();
        return new WeakReference<object>(box);
    }

    private static async Task VerifyTableResizeAndPartialInvalidateAsync(SelfTestContext context)
    {
        context.Stage.Children.Clear();
        var root = new VariableXamlRoot
        {
            DeferUpdates = false,
            MeasureLayout = true,
            Width = Math.Min(900, context.Stage.XamlRoot.Size.Width - 8),
            Height = Math.Min(500, context.Stage.XamlRoot.Size.Height - 8),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        MarkdownElements.Configure(root);
        context.Stage.Children.Add(root);
        await Task.Delay(50);

        const string smallMarkdown = """
            | A | B |
            | --- | --- |
            | 1 | 2 |
            """;
        var largeBuilder = new System.Text.StringBuilder();
        largeBuilder.AppendLine("| Column Header 1 | Column Header 2 | Column Header 3 | Column Header 4 |");
        largeBuilder.AppendLine("| :--- | :---: | ---: | :--- |");
        for (int r = 1; r <= 7; r++)
        {
            largeBuilder.AppendLine($"| Row {r} Alpha | Cell {r}-2 Middle | Cell {r}-3 Value | Trailing text {r} |");
        }

        string largeMarkdown = largeBuilder.ToString();
        static ProjectionResult ProjectFresh(string markdown)
        {
            var ids = new SequentialBlockIdSource();
            Document doc = MarkdownImporter.Import(markdown, ids).Document;
            return new DocumentProjector().Project(EditorState.AtStart(doc));
        }

        // 1. Render and draw a compact 2x2 table.
        root.Render(ProjectFresh(smallMarkdown).Root);
        root.Flush();
        root.UpdateLayout();
        await WaitForDrawsAsync(root);
        MdTable? table = FindDescendant<MdTable>(root);
        context.Check(table is { LastDrawError: null }, "render: compact 2x2 table mounts cleanly before resize");
        if (table is null)
        {
            context.Stage.Children.Clear();
            return;
        }

        // 2. Patch the same table key in place to a larger 8x4 table, then immediately trigger
        // partial invalidation on the bottom-right cell before Win2D catches up on resize.
        root.Render(ProjectFresh(largeMarkdown).Root);
        root.Flush();
        root.UpdateLayout();
        MdTable? grownTable = FindDescendant<MdTable>(root);
        bool sameTableInstance = ReferenceEquals(table, grownTable);
        List<MdTableCell> grownCells = grownTable is null ? [] : FindAll<MdTableCell>(grownTable).ToList();
        MdTableCell? lastCell = grownCells.LastOrDefault();
        bool immediateGrowNoThrow = true;
        try
        {
            if (lastCell is not null)
            {
                lastCell.SelectionStart = 0;
                lastCell.SelectionEnd = 2;
                grownTable?.InvalidateDelegatedChild(lastCell);
            }
        }
        catch (Exception)
        {
            immediateGrowNoThrow = false;
        }

        await WaitForDrawsAsync(root);
        bool allGrownCellsDrawn = grownCells.Count == 32
            && grownCells.All(static c => c.DrawCount > 0 && c.LastDrawError is null);
        context.Check(
            sameTableInstance && immediateGrowNoThrow && grownTable is { LastDrawError: null } && allGrownCellsDrawn,
            "render: growing table in place + immediate partial cell invalidation never throws",
            $"sameInstance={sameTableInstance}, noThrow={immediateGrowNoThrow}, cells={grownCells.Count}, drawn={grownCells.Count(static c => c.DrawCount > 0)}, error={grownTable?.LastDrawError?.Message}");

        // 3. Verify partial invalidation still triggers a draw once the larger surface has settled.
        int tableDrawsBefore = table.DrawCount;
        int cellDrawsBefore = lastCell?.DrawCount ?? 0;
        if (lastCell is not null)
        {
            lastCell.SelectionStart = 1;
            lastCell.SelectionEnd = 3;
            table.InvalidateDelegatedChild(lastCell);
        }

        await WaitUntilAsync(() => table.DrawCount > tableDrawsBefore && (lastCell?.DrawCount ?? 0) > cellDrawsBefore, 3000);
        context.Check(
            table.LastDrawError is null && table.DrawCount > tableDrawsBefore && (lastCell?.DrawCount ?? 0) > cellDrawsBefore,
            "render: settled 8x4 table supports partial delegated cell redraw",
            $"tableDraws={tableDrawsBefore}->{table.DrawCount}, cellDraws={cellDrawsBefore}->{lastCell?.DrawCount ?? 0}");

        // 4. Shrink back to 2x2 in place and invalidate immediately.
        root.Render(ProjectFresh(smallMarkdown).Root);
        root.Flush();
        root.UpdateLayout();
        MdTable? shrunkTable = FindDescendant<MdTable>(root);
        List<MdTableCell> shrunkCells = shrunkTable is null ? [] : FindAll<MdTableCell>(shrunkTable).ToList();
        MdTableCell? shrunkLastCell = shrunkCells.LastOrDefault();
        bool immediateShrinkNoThrow = true;
        try
        {
            if (shrunkLastCell is not null)
            {
                shrunkLastCell.SelectionStart = 0;
                shrunkLastCell.SelectionEnd = 1;
                shrunkTable?.InvalidateDelegatedChild(shrunkLastCell);
            }
        }
        catch (Exception)
        {
            immediateShrinkNoThrow = false;
        }

        await WaitForDrawsAsync(root);
        bool allShrunkCellsDrawn = shrunkCells.Count == 4
            && shrunkCells.All(static c => c.DrawCount > 0 && c.LastDrawError is null);
        context.Check(
            ReferenceEquals(table, shrunkTable) && immediateShrinkNoThrow && shrunkTable is { LastDrawError: null } && allShrunkCellsDrawn,
            "render: shrinking table in place + immediate partial cell invalidation never throws",
            $"sameInstance={ReferenceEquals(table, shrunkTable)}, noThrow={immediateShrinkNoThrow}, cells={shrunkCells.Count}, drawn={shrunkCells.Count(static c => c.DrawCount > 0)}, error={shrunkTable?.LastDrawError?.Message}");

        context.Stage.Children.Clear();
    }

    private static void RenderCorpus(SelfTestContext context, VariableXamlRoot root, string name, IReadOnlyList<SpecExample> examples)
    {
        if (examples.Count == 0)
        {
            context.Check(false, $"render: the {name} corpus is found under testdata");
            return;
        }

        var failures = new List<string>();
        int xamlReaderLoads = 0;
        int blocks = 0;
        var watch = Stopwatch.StartNew();
        foreach (var example in examples)
        {
            context.Trace($"{name} #{example.Number}");
            try
            {
                var state = EditorDocuments.Load(example.Markdown);
                var result = new DocumentProjector().Project(state);
                root.Reset();
                root.Render(result.Root);
                root.Flush();
                var report = root.LastReport!;
                xamlReaderLoads += report.Statistics.XamlReaderLoads;
                int expected = CountBlocks(result.Root);
                int realized = CountRealized(root);
                blocks += realized;
                if (report.Error is not null || report.Diagnostics.Count > 0 || realized != expected)
                {
                    string detail = report.Error?.Message ?? (report.Diagnostics.Count > 0 ? report.Diagnostics[0] : $"{realized} of {expected} blocks realized");
                    failures.Add($"#{example.Number} ({example.Section}): {detail}");
                }
            }
            catch (Exception exception)
            {
                failures.Add($"#{example.Number} ({example.Section}): {exception.GetType().Name}: {exception.Message}");
            }
        }

        context.Check(failures.Count == 0, $"render: all {examples.Count} {name} examples mount without diagnostics or placeholders, every block realized",
            failures.Count == 0 ? $"{blocks} blocks in {watch.ElapsedMilliseconds} ms" : $"{failures.Count} failures, first: {string.Join(" | ", failures.Take(3))}");
        context.Check(xamlReaderLoads == 0, $"render: {name} elements are created by registered factories, never by XamlReader", $"{xamlReaderLoads} loads");
    }

    private static async Task RenderLongDocumentAsync(SelfTestContext context, VariableXamlRoot root)
    {
        if (SpecCorpora.GfmSpecPath() is not { } path)
        {
            context.Check(false, "render: the GFM specification is found under testdata");
            return;
        }

        var state = EditorDocuments.Load(File.ReadAllText(path));
        var projector = new DocumentProjector();
        var watch = Stopwatch.StartNew();
        var result = projector.Project(state);
        double projection = watch.Elapsed.TotalMilliseconds;
        root.Reset();
        watch.Restart();
        root.Render(result.Root);
        root.Flush();
        double mount = watch.Elapsed.TotalMilliseconds;
        int topLevel = result.Root.Children[0].Children.Count;
        int realized = CountRealized(root);
        context.Check(root.LastReport is { Error: null, Diagnostics.Count: 0 } && realized > 0 && realized < CountBlocks(result.Root),
            "render: the GFM specification mounts in the virtual stack, only blocks near the viewport realized",
            $"{topLevel} top-level blocks, {realized} block controls realized; projection {projection:F0} ms, mount and layout {mount:F0} ms");

        var document = FindDescendant<MdDocument>(root);
        var paragraph = FindDescendant<MdParagraph>(root);
        context.Check(document is not null && paragraph is not null, "render: the document and its paragraphs are Md* controls");
        if (document is null || paragraph is null)
        {
            return;
        }

        var waited = Stopwatch.StartNew();
        // Any paragraph in view: the first one in the tree may be one the stack keeps out of view.
        bool Drawn() => FindAll<MdParagraph>(root).Any(static view => view.DrawCount > 0);
        // The first editor probe of the run: its first frame includes the cold start of Win2D and the window.
        await WaitUntilAsync(Drawn, 6000);
        context.Info($"first draw of a realized paragraph after {waited.ElapsedMilliseconds} ms (Win2D surfaces draw on a later frame)");
        context.Check(Drawn(), "render: a realized paragraph draws itself on its canvas", $"{FindAll<MdParagraph>(root).Sum(static view => view.DrawCount)} draws");
        context.Check(paragraph.Context.Surface == MdStyleSheet.Light.PageBackground && paragraph.Context.Sheet == document.EffectiveStyleSheet,
            "render: blocks resolve the style sheet and surface color from the document");

        // Theme switch: the same VNode stays applied; blocks relayout and redraw from the new sheet.
        long sequence = root.LastReport!.Sequence;
        int draws = paragraph.DrawCount;
        double height = paragraph.ActualHeight;
        document.StyleSheet = MdStyleSheet.Dark;
        root.UpdateLayout();
        await WaitUntilAsync(() => paragraph.DrawCount > draws, 3000);
        context.Check(root.LastReport!.Sequence == sequence && ReferenceEquals(root.CurrentTree, result.Root),
            "render: switching the theme does not re-project or re-render the tree (05)");
        context.Check(paragraph.Context.Sheet == MdStyleSheet.Dark && paragraph.Context.Surface == MdStyleSheet.Dark.PageBackground && paragraph.DrawCount > draws,
            "render: realized blocks pick up the dark sheet and redraw", $"draws {draws} -> {paragraph.DrawCount}, height {height:F1} -> {paragraph.ActualHeight:F1}");
        document.StyleSheet = MdStyleSheet.HighContrast();
        root.UpdateLayout();
        context.Check(paragraph.Context.Surface == document.EffectiveStyleSheet.PageBackground, "render: the high-contrast sheet comes from the system colors");
        document.StyleSheet = null;
        root.UpdateLayout();

        // Scrolling realizes blocks further down from the same tree.
        document.ScrollViewer.ChangeView(null, document.ScrollViewer.ScrollableHeight / 2, null, disableAnimation: true);
        await Task.Delay(300);
        root.UpdateLayout();
        context.Check(root.LastReport!.Sequence == sequence && FindDescendant<MdBlockView>(root) is not null,
            "render: scrolling to the middle realizes blocks there without a new render", $"scrollable height {document.ScrollViewer.ScrollableHeight:F0}");
    }

    /// <summary>The showcase document in the light and dark sheets, saved as pictures for review.</summary>
    private static async Task CaptureShowcaseAsync(SelfTestContext context, VariableXamlRoot root)
    {
        var result = new DocumentProjector().Project(EditorDocuments.Load(MarkdownShowcase.Text));
        // Win2D virtual surfaces only draw what lies inside the window, so the capture fits the window.
        root.Reset();
        root.Width = Math.Min(900, root.XamlRoot.Size.Width - 8);
        root.Height = root.XamlRoot.Size.Height - 8;
        root.Render(result.Root);
        root.Flush();
        context.Check(root.LastReport is { Error: null, Diagnostics.Count: 0 }, "showcase: every element of the showcase mounts without diagnostics",
            root.LastReport?.Diagnostics.FirstOrDefault());
        if (FindDescendant<MdDocument>(root) is not { } document)
        {
            return;
        }

        // Paging to the end needs anchoring off: with variable block heights the stack re-estimates the
        // blocks it virtualized, and anchoring then keeps ChangeView short of the end (known issue, M5.3).
        document.ScrollViewer.VerticalAnchorRatio = double.NaN;
        string folder = RepositoryPaths.Temp("m5-1");
        var files = new List<string>();
        foreach (var (name, sheet) in new[] { ("light", MdStyleSheet.Light), ("dark", MdStyleSheet.Dark) })
        {
            document.StyleSheet = sheet;
            document.ScrollViewer.ChangeView(null, 0, null, disableAnimation: true);
            root.UpdateLayout();
            double page = root.Height - 40;

            // The stack estimates the extent of blocks it has not realized: read it again at every step.
            for (int index = 0; index < 12 && (index == 0 || document.ScrollViewer.VerticalOffset < document.ScrollViewer.ScrollableHeight - 1); index++)
            {
                var scroll = document.ScrollViewer;
                scroll.ChangeView(null, index * page, null, disableAnimation: true);
                await WaitUntilAsync(() => Math.Abs(scroll.VerticalOffset - Math.Min(index * page, scroll.ScrollableHeight)) < 1, 2000);
                root.UpdateLayout();
                await WaitForDrawsAsync(root);
                string file = Path.Combine(folder, $"showcase-{name}-{index + 1}.png");
                await SaveAsync(root, file);
                files.Add(Path.GetFileName(file));
            }
        }

        document.StyleSheet = null;
        document.ScrollViewer.VerticalAnchorRatio = 0;
        var scroller = document.ScrollViewer;
        var tail = Blocks(root).Where(block => block.Parent is not MdBlockView).Select(block =>
        {
            var top = block.TransformToVisual(scroller).TransformPoint(default);
            return $"{block.GetType().Name}@{top.Y:F0}+{block.ActualHeight:F0}";
        });
        context.Info($"showcase scroll: extent {scroller.ExtentHeight:F0}, viewport {scroller.ViewportHeight:F0}, offset {scroller.VerticalOffset:F0}; realized top-level blocks: {string.Join(", ", tail)}");
        context.Info($"showcase pictures: {string.Join(", ", files)} in {folder}");
    }

    /// <summary>Waits until every realized block that draws has drawn at least once since the last check.</summary>
    internal static async Task WaitForDrawsAsync(DependencyObject root)
    {
        var before = Blocks(root).ToDictionary(block => block, block => block.DrawCount);
        await WaitUntilAsync(() => Blocks(root).All(block => !before.TryGetValue(block, out int count) || block.DrawCount > count || !HasCanvas(block)), 3000);
        await Task.Delay(100);
    }

    private static bool HasCanvas(MdBlockView block) =>
        VisualTreeHelper.GetChildrenCount(block) > 0 && VisualTreeHelper.GetChild(block, 0) is Microsoft.Graphics.Canvas.UI.Xaml.CanvasVirtualControl;

    private static IEnumerable<MdBlockView> Blocks(DependencyObject root)
    {
        int children = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < children; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is MdBlockView block)
            {
                yield return block;
            }

            foreach (var nested in Blocks(child))
            {
                yield return nested;
            }
        }
    }

    internal static async Task SaveAsync(UIElement element, string path)
    {
        var capture = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
        await capture.RenderAsync(element);
        byte[] pixels = (await capture.GetPixelsAsync()).ToArray();
        var device = Microsoft.Graphics.Canvas.CanvasDevice.GetSharedDevice();
        using var bitmap = Microsoft.Graphics.Canvas.CanvasBitmap.CreateFromBytes(device, pixels, capture.PixelWidth, capture.PixelHeight,
            Windows.Graphics.DirectX.DirectXPixelFormat.B8G8R8A8UIntNormalized);
        await bitmap.SaveAsync(path);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < timeoutMilliseconds)
        {
            await Task.Delay(20);
        }
    }

    /// <summary>The block elements of a projected tree: everything inside the document node but the virtual stack itself.</summary>
    private static int CountBlocks(VNode node)
    {
        int count = 0;
        foreach (var child in node.Children)
        {
            if (!child.IsText)
            {
                count += (child.Type == ProjectionNames.VirtualStack ? 0 : 1) + CountBlocks(child);
            }
        }

        return count;
    }

    private static int CountRealized(DependencyObject root)
    {
        int count = 0;
        int children = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < children; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is MdBlockView { Visibility: Visibility.Visible } block && block.Parent is not null)
            {
                count++;
            }

            count += CountRealized(child);
        }

        return count;
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        int children = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < children; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            if (FindDescendant<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private static IEnumerable<T> FindAll<T>(DependencyObject root)
        where T : DependencyObject
    {
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T found)
            {
                yield return found;
            }

            foreach (var nested in FindAll<T>(child))
            {
                yield return nested;
            }
        }
    }
}
