using Microsoft.UI.Xaml;
using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Services;
using Quill.Editor.State;
using Quill.Editor.WinUI.Elements;
using Quill.Playground.Samples;
using Quill.VariableXaml;
using Windows.Foundation;

namespace Quill.Playground.SelfTest;

/// <summary>
/// M5.2, layer 2 (08): the real layout service and caret overlay on the showcase document at a width
/// where paragraphs wrap. Properties over every caret position of every block in view: hit testing
/// the caret rectangle returns the same visual place, visual lines agree with caret rectangles and
/// hit tests at their ends; Down and Up walk the lines in order, stop on block carets and keep the
/// goal X when leaving them; the overlay draws the caret where the layout service puts it.
/// </summary>
internal static class LayoutServiceProbes
{
    private const double Tolerance = 0.5;

    public static async Task RunAsync(SelfTestContext context)
    {
        context.Stage.Children.Clear();
        var root = new VariableXamlRoot
        {
            DeferUpdates = false,
            Width = 520,
            Height = Math.Min(500, context.Stage.XamlRoot.Size.Height - 8),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        MarkdownElements.Configure(root);
        context.Stage.Children.Add(root);
        await Task.Delay(50);
        var rendered = new RenderedSession(root, EditorDocuments.Load(MarkdownShowcase.Text));

        CheckRoundTrips(context, rendered);
        // A freshly mounted ScrollViewer applies view changes only after the window yielded once;
        // in the app the document is always shown before any input arrives.
        await Task.Delay(100);
        var scroller = rendered.Document.ScrollViewer;
        rendered.Layout.ScrollRectIntoView(new LayoutRect(0, 900, 1, 20));
        // Anchoring may shift the offset while the stack re-estimates heights (Q69); the rectangle must be in view.
        var shown = rendered.Layout.GetViewport();
        context.Check(shown.Y > 0 && shown.Y <= 900 && shown.Bottom >= 920, "layout: ScrollRectIntoView brings the rectangle into view, synchronously",
            $"viewport {shown.Y:F0}..{shown.Bottom:F0} of {scroller.ExtentHeight:F0}");
        rendered.Layout.ScrollRectIntoView(new LayoutRect(0, 0, 1, 20));
        CheckVerticalMoves(context, rendered);
        CheckBlockCaretGoalX(context);
        await CheckOverlayAsync(context, rendered);
        await CheckStackLayoutAsync(context, rendered);
        CheckScrollIntoView(context, rendered);
        await CheckOutOfTreeAsync(context, rendered);
        context.Stage.Children.Clear();
    }

    /// <summary>HitTest(GetCaretRect(p)) is p's visual place; visual lines agree with carets and hit tests.</summary>
    private static void CheckRoundTrips(SelfTestContext context, RenderedSession rendered)
    {
        var layout = rendered.Layout;
        var viewport = layout.GetViewport();
        var failures = new List<string>();
        var lineFailures = new List<string>();
        int positions = 0;
        int blocks = 0;
        var wrapped = 0;
        foreach (var view in rendered.Document.Geometry.Realized.OfType<MdTextBlockView>())
        {
            if (view.TextLayout is not { } text || Quill.Editor.WinUI.Layout.BlockGeometry.ParseKey(view.BlockId) is not { } id || rendered.Projection!.Map[id] is not { } map
                || layout.GetBlockBounds(id) is not { } bounds || bounds.Bottom < viewport.Y || bounds.Y > viewport.Bottom)
            {
                continue;
            }

            blocks++;
            wrapped += text.LineCount > 1 ? 1 : 0;

            // The caret stands only on grapheme boundaries (02).
            var boundaries = new HashSet<int>(System.Globalization.StringInfo.ParseCombiningCharacters(text.Text)) { text.Text.Length };
            for (int display = 0; display <= map.DisplayLength; display++)
            {
                if (!boundaries.Contains(display))
                {
                    continue;
                }

                foreach (var affinity in new[] { Affinity.Forward, Affinity.Backward })
                {
                    var position = new TextPosition(id, map.ToSource(display, affinity), affinity);
                    if (map.ToDisplay(position.Offset, affinity) != display)
                    {
                        continue;
                    }

                    positions++;
                    var caret = layout.GetCaretRect(position);
                    var hit = layout.HitTest(new LayoutPoint(caret.X, caret.Y + caret.Height / 2));
                    if (!SameVisualPlace(layout, rendered, position, hit))
                    {
                        string around = text.Text.Substring(Math.Max(0, display - 3), Math.Min(text.Text.Length, display + 3) - Math.Max(0, display - 3));
                        failures.Add($"{position} (display {display} in \"{around.Replace("\n", "|")}\") -> {hit}");
                    }

                    var line = layout.GetVisualLine(position);
                    var start = layout.GetCaretRect(line.Start);
                    var end = layout.GetCaretRect(line.End);
                    double middle = line.Bounds.Y + line.Bounds.Height / 2;
                    var leftHit = layout.HitTest(new LayoutPoint(line.Bounds.X - 20, middle));
                    var rightHit = layout.HitTest(new LayoutPoint(line.Bounds.X + line.Bounds.Width + 20, middle));
                    if (Math.Abs(start.Y - caret.Y) > Tolerance && !IsScript(caret, start) || Math.Abs(end.Bottom - caret.Bottom) > caret.Height
                        || !SameVisualPlace(layout, rendered, line.Start, leftHit) || !SameVisualPlace(layout, rendered, line.End, rightHit))
                    {
                        lineFailures.Add($"{position}: line {line.Start}..{line.End}, hits {leftHit}..{rightHit}");
                    }
                }
            }
        }

        context.Check(positions > 200 && failures.Count == 0, "layout: HitTest(GetCaretRect(p)) returns p's visual place for every position in view",
            failures.Count == 0 ? $"{positions} positions in {blocks} blocks, {wrapped} of them wrapped" : $"{failures.Count} of {positions}, first: {string.Join("; ", failures.Take(4))}");
        context.Check(lineFailures.Count == 0, "layout: visual line ends share the caret's line and are what hit tests at the line ends return",
            lineFailures.Count == 0 ? null : $"{lineFailures.Count} failures, first: {string.Join("; ", lineFailures.Take(3))}");
    }

    /// <summary>Down and Up visit lines in order through the whole document; Up from the end comes back.</summary>
    private static void CheckVerticalMoves(SelfTestContext context, RenderedSession rendered)
    {
        rendered.Execute(new SetSelection(TextSelection.Caret(EditorDocuments.FirstCaret(rendered.Session.State.Document))));
        var trail = new List<string>();
        double previous = double.NegativeInfinity;
        bool ordered = true;
        int blockCarets = 0;
        int steps = 0;
        for (; steps < 400; steps++)
        {
            var before = rendered.Session.State.Selection;
            rendered.Press(MoveDirection.Down);
            var after = rendered.Session.State.Selection;
            if (after == before)
            {
                break;
            }

            if (after.IsBlockCaret)
            {
                blockCarets++;
                continue;
            }

            double y = rendered.Layout.GetCaretRect(after.Head).Y;
            if (y < previous - Tolerance)
            {
                ordered = false;
                var gaps = rendered.Document.Geometry.Realized.OfType<MdVirtualParagraph>()
                    .Select(gap => $"{gap.BlockId}[{gap.Number}]@{rendered.Document.Geometry.Bounds(gap).Y:F0}");
                trail.Add($"{before.Head} -> {after.Head} at y {y:F1} < {previous:F1} (viewport {rendered.Layout.GetViewport().Y:F0}, offset {rendered.Document.ScrollViewer.VerticalOffset:F0} of {rendered.Document.ScrollViewer.ScrollableHeight:F0}, gaps {string.Join(",", gaps)})");
            }

            previous = y;
        }

        var end = rendered.Session.State.Selection.Head;
        context.Check(ordered && steps < 400 && blockCarets >= 2, "layout: Down walks every line top to bottom, stopping on the rule and the TOC (block carets)",
            $"{steps} moves, {blockCarets} block carets, ends at {end}{(trail.Count > 0 ? "; " + string.Join("; ", trail.Take(3)) : string.Empty)}");

        int upSteps = 0;
        for (; upSteps < 400; upSteps++)
        {
            var before = rendered.Session.State.Selection;
            rendered.Press(MoveDirection.Up);
            if (rendered.Session.State.Selection == before)
            {
                break;
            }
        }

        // Up goes one stop further: the virtual paragraph before the front matter at the document start (02).
        var start = rendered.Session.State.Selection.Head;
        bool atStart = start is BoundaryPosition { Index: 0 } boundary && boundary.Parent == rendered.Session.State.Document.Id;
        context.Check(atStart && (upSteps == steps || upSteps == steps + 1), "layout: Up walks back through the same lines to the document start",
            $"{upSteps} moves up, {steps} down, ends at {start}");
    }

    /// <summary>Leaving a block caret by Up or Down keeps the goal X in the real layout (M5.2).</summary>
    private static void CheckBlockCaretGoalX(SelfTestContext context)
    {
        var root = (VariableXamlRoot)context.Stage.Children[0];
        var rendered = new RenderedSession(root, EditorDocuments.Load("Above the rule, a line of text.\n\n---\n\nBelow the rule, another line."));
        var document = rendered.Session.State.Document;
        var above = document.Children[0].Id;
        var below = document.Children[2].Id;
        rendered.Execute(new SetSelection(TextSelection.Caret(above, 10)));
        double x = rendered.Layout.GetCaretRect(rendered.Session.State.Selection.Head).X;
        rendered.Press(MoveDirection.Down);
        bool onRule = rendered.Session.State.Selection.IsBlockCaret && rendered.Document.CaretBounds is null;
        rendered.Press(MoveDirection.Down);
        var head = rendered.Session.State.Selection.Head;
        double landed = rendered.Layout.GetCaretRect(head).X;
        context.Check(onRule && head is TextPosition { Block: var block } && block == below && Math.Abs(landed - x) < 10,
            "layout: Down onto the rule shows no caret; Down again leaves it at the goal X", $"x {x:F1} -> {landed:F1} at {head}");
    }

    /// <summary>The overlay draws the caret where the layout service puts it, and hides it for a selection.</summary>
    private static async Task CheckOverlayAsync(SelfTestContext context, RenderedSession rendered)
    {
        rendered.Execute(new SetSelection(TextSelection.Caret(EditorDocuments.FirstCaret(rendered.Session.State.Document))));
        int matches = 0;
        var mismatches = new List<string>();
        for (int i = 0; i < 12; i++)
        {
            rendered.Press(MoveDirection.Forward, TextUnit.Word);
            var head = rendered.Session.State.Selection.Head;
            var expected = rendered.Layout.GetCaretRect(head);
            if (rendered.Document.CaretBounds is { } drawn && Math.Abs(drawn.X - expected.X) < Tolerance && Math.Abs(drawn.Y - expected.Y) < Tolerance && Math.Abs(drawn.Height - expected.Height) < Tolerance)
            {
                matches++;
            }
            else
            {
                mismatches.Add($"{head}: drawn {rendered.Document.CaretBounds?.ToString() ?? "none"}, layout {expected}");
            }
        }

        context.Check(mismatches.Count == 0, "overlay: the caret is drawn at the layout service's caret rectangle after every move",
            mismatches.Count == 0 ? $"{matches} moves" : string.Join("; ", mismatches.Take(3)));

        rendered.Execute(new MoveCaret(MoveDirection.Forward, TextUnit.Word, Extend: true));
        bool hidden = rendered.Document.CaretBounds is null;
        rendered.Press(MoveDirection.Forward);
        await Task.Delay(50);
        context.Check(hidden && rendered.Document.CaretBounds is not null, "overlay: no caret over a selection; it comes back when the selection collapses");
    }

    /// <summary>A block far down a long document: ScrollIntoView realizes it in the viewport without pinning (Q64).</summary>
    /// <summary>Q69: the block layout keeps measured heights, so the end is reachable with anchoring on and the extent is stable.</summary>
    private static async Task CheckStackLayoutAsync(SelfTestContext context, RenderedSession rendered)
    {
        var scroller = rendered.Document.ScrollViewer;
        context.Check(rendered.Document.Body is Microsoft.UI.Xaml.Controls.ItemsRepeater { Layout: Quill.Editor.WinUI.Layout.BlockStackLayout },
            "stack: the virtual stack uses the block layout that keeps measured heights (Q69)");
        var extents = new List<double>();
        for (int i = 0; i < 6; i++)
        {
            scroller.ChangeView(null, scroller.ScrollableHeight, null, disableAnimation: true);
            await Task.Delay(150);
            extents.Add(scroller.ExtentHeight);
        }

        bool atEnd = Math.Abs(scroller.VerticalOffset - scroller.ScrollableHeight) < 1;
        scroller.ChangeView(null, 0, null, disableAnimation: true);
        await Task.Delay(150);
        extents.Add(scroller.ExtentHeight);
        context.Check(atEnd && extents.Skip(1).All(extent => Math.Abs(extent - extents[1]) < 1),
            "stack: with anchoring on, scrolling to the end reaches it and the extent stays put once blocks were measured",
            $"offset {scroller.VerticalOffset:F0}, extents {string.Join(", ", extents.Select(extent => extent.ToString("F0")))}");
    }

    /// <summary>Q71: queries about blocks the stack has not realized are answered outside the tree, without scrolling.</summary>
    private static async Task CheckOutOfTreeAsync(SelfTestContext context, RenderedSession previous)
    {
        if (SpecCorpora.GfmSpecPath() is not { } path)
        {
            return;
        }

        var rendered = new RenderedSession(previous.Root, EditorDocuments.Load(File.ReadAllText(path)));
        await Task.Delay(100);

        // Blocks measured after the load replace estimates and scroll anchoring moves the offset: wait until it rests.
        for (int i = 0, quiet = 0; i < 30 && quiet < 2; i++)
        {
            double before = rendered.Document.ScrollViewer.VerticalOffset;
            await Task.Delay(150);
            quiet = Math.Abs(rendered.Document.ScrollViewer.VerticalOffset - before) < 0.5 ? quiet + 1 : 0;
        }
        var layout = rendered.Layout;
        var scroller = rendered.Document.ScrollViewer;
        var document = rendered.Session.State.Document;
        var far = document.Children.Select((block, index) => (block, index)).Skip(document.Children.Count / 2)
            .First(pair => pair.block is Paragraph { Text.Length: > 120 });
        var position = new TextPosition(far.block.Id, 60);
        double offset = scroller.VerticalOffset;
        bool realized = layout.GetBlockBounds(far.block.Id) is not null;
        var caret = layout.GetCaretRect(position);
        int detached = layout.DetachedLayouts;
        var line = layout.GetVisualLine(position);
        var hit = layout.HitTest(new LayoutPoint(caret.X, caret.Y + caret.Height / 2));
        var below = layout.GetAdjacentLine(position, caret.X, down: true);
        context.Check(!realized && Math.Abs(scroller.VerticalOffset - offset) < 0.5 && caret.Height > 0 && hit is TextPosition { Block: var hitBlock, Offset: 60 } && hitBlock == far.block.Id,
            "out of tree: caret rectangle, visual line and hit test of a far, unrealized paragraph, without scrolling (Q71)",
            $"caret {caret}, line {line.Start}..{line.End}, hit {hit}, detached layouts {detached}, offset {offset:F0} -> {scroller.VerticalOffset:F0}");
        context.Check(below is TextPosition && Math.Abs(scroller.VerticalOffset - offset) < 0.5, "out of tree: the line below a far paragraph", below?.ToString());

        // Realized later, the block's own geometry is the same; only its top moves where estimates were replaced.
        layout.ScrollIntoView(far.block.Id);
        var realizedCaret = layout.GetCaretRect(position);
        var top = layout.GetBlockBounds(far.block.Id);
        context.Check(top is { } bounds && Math.Abs(realizedCaret.X - caret.X) < 0.5 && Math.Abs(realizedCaret.Height - caret.Height) < 0.5,
            "out of tree: the detached layout matches the realized block (x and caret height)",
            $"detached {caret}, realized {realizedCaret}, block top {top?.Y:F1}");
    }

    private static void CheckScrollIntoView(SelfTestContext context, RenderedSession previous)
    {
        if (SpecCorpora.GfmSpecPath() is not { } path)
        {
            return;
        }

        var rendered = new RenderedSession(previous.Root, EditorDocuments.Load(File.ReadAllText(path)));
        var document = rendered.Session.State.Document;
        var target = document.Children[document.Children.Count * 3 / 4].Id;
        bool realizedBefore = rendered.Layout.GetBlockBounds(target) is not null;
        rendered.Layout.ScrollIntoView(target);
        var viewport = rendered.Layout.GetViewport();
        var bounds = rendered.Layout.GetBlockBounds(target);
        context.Check(!realizedBefore && bounds is { } b && b.Bottom > viewport.Y && b.Y < viewport.Bottom,
            "layout: ScrollIntoView brings an unrealized block three quarters down into the viewport, synchronously",
            $"viewport {viewport.Y:F0}..{viewport.Bottom:F0}, block {(bounds is { } r ? $"{r.Y:F0}..{r.Bottom:F0}" : "not realized")}");

        // Caret queries about it now answer from the realized block.
        var caret = rendered.Layout.GetCaretRect(new TextPosition(FirstLeaf(document, target), 0));
        context.Check(caret.Height > 0 && caret.Y >= viewport.Y - 1 && caret.Y <= viewport.Bottom, "layout: the caret rectangle of a block brought into view lies in the viewport", caret.ToString());
    }

    private static BlockId FirstLeaf(Document document, BlockId id)
    {
        var block = DocumentLookup.Find(document, id);
        while (block is ContainerBlock { Children.Count: > 0 } container)
        {
            block = container.Children[0];
        }

        return block!.Id;
    }

    private static bool IsScript(LayoutRect caret, LayoutRect start) => caret.Height < start.Height - Tolerance || start.Height < caret.Height - Tolerance;

    /// <summary>Two positions show the caret at the same place: same block, same display offset and the same line.</summary>
    private static bool SameVisualPlace(Quill.Editor.WinUI.Layout.XamlLayoutService layout, RenderedSession rendered, Position expected, Position actual)
    {
        if (expected is not TextPosition a || actual is not TextPosition b || a.Block != b.Block || rendered.Projection!.Map[a.Block] is not { } map)
        {
            return expected.SamePlace(actual);
        }

        if (map.ToDisplay(a.Offset, a.Affinity) != map.ToDisplay(b.Offset, b.Affinity))
        {
            return false;
        }

        // The same display offset at the same x on the same line. Next to a hidden marker both
        // affinities show there; one may be the smaller caret of a script (10), so heights may differ.
        var left = layout.GetCaretRect(a);
        var right = layout.GetCaretRect(b);
        return Math.Abs(left.X - right.X) < Tolerance && layout.GetVisualLine(a).Bounds == layout.GetVisualLine(b).Bounds;
    }
}
