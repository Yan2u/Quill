using System.Numerics;
using Microsoft.Graphics.Canvas;
using Quill.Editor.Projection;
using Windows.Foundation;

namespace Quill.Editor.WinUI.Elements;

/// <summary>
/// A table. Columns take the natural width of their widest cell; when the table is wider than the
/// page, columns shrink toward an equal share and their cells wrap. The grid lines are the table's
/// own surface showing through one-pixel gaps between the opaque cells. The toolbar of the active
/// table comes with the other adornments (M5.5).
/// </summary>
public sealed partial class MdTable : MdContainerView
{
    private const double Border = 1;
    private const double MinimumColumn = 48;
    private const int NaturalWidthCacheCapacity = 256;

    private static readonly Lock NaturalWidthGate = new();
    private static readonly Dictionary<TableNaturalWidthKey, LinkedListNode<NaturalWidthEntry>> NaturalWidthCache = [];
    private static readonly LinkedList<NaturalWidthEntry> NaturalWidthOrder = new();

    private double[] _columns = [];
    private Size _gridSize;
    private bool _measuring;

    public MdTable()
        : base(draws: true)
    {
    }

    /// <summary>The column alignments as projected (<c>Left,None,…</c>); the cells align themselves.</summary>
    public string Alignments { get; set; } = string.Empty;

    /// <summary>The width of the drawn grid (the table may be narrower than its block).</summary>
    internal double GridWidth => _gridSize.Width;

    /// <summary>The resolved column widths from the latest measure pass.</summary>
    internal IReadOnlyList<double> ColumnWidths => _columns;

    internal void InvalidateColumnWidths()
    {
        if (!_measuring)
        {
            InvalidateMeasure();
        }
    }

    public override double FirstBaseline =>
        Blocks.Count > 0 && Blocks[0] is MdBlockView first ? Border + first.FirstBaseline : base.FirstBaseline;

    protected override Size MeasureBlock(Size availableSize)
    {
        _measuring = true;
        try
        {
            int body = 0;
            foreach (var row in Rows())
            {
                row.IsStriped = !row.IsHeader && body++ % 2 == 1;
            }

            bool cacheable = TryBuildNaturalWidthKey(out var cacheKey, out int count);
            double[] natural;
            if (cacheable && TryGetCachedNaturalWidths(cacheKey, out double[]? cachedNatural))
            {
                natural = cachedNatural;
            }
            else
            {
                if (!cacheable)
                {
                    count = 0;
                    foreach (var row in Rows())
                    {
                        count = Math.Max(count, row.Blocks.Count);
                    }
                }

                natural = new double[count];
                bool allCellsMatchSheet = true;
                foreach (var row in Rows())
                {
                    for (int i = 0; i < row.Blocks.Count; i++)
                    {
                        if (row.Blocks[i] is MdTextBlockView cell)
                        {
                            natural[i] = Math.Max(natural[i], Math.Max(MinimumColumn, cell.MeasureNaturalWidth()));
                            if (!ReferenceEquals(cell.Context.Sheet, Sheet))
                            {
                                allCellsMatchSheet = false;
                            }
                        }
                    }
                }

                if (cacheable && allCellsMatchSheet)
                {
                    RememberNaturalWidths(cacheKey, natural);
                }
            }

            double available = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : availableSize.Width - (count + 1) * Border;
            _columns = Fit(natural, available);
            double height = Border;
            double width = _columns.Sum() + (count + 1) * Border;
            foreach (var row in Rows())
            {
                row.ColumnWidths = _columns;
                row.Measure(new Size(width - 2 * Border, double.PositiveInfinity));
                height += row.DesiredSize.Height + Border;
            }

            _gridSize = new Size(width, height);
            return new Size(double.IsInfinity(availableSize.Width) ? width : availableSize.Width, height);
        }
        finally
        {
            _measuring = false;
        }
    }

    protected override void ArrangeBlock(Size finalSize)
    {
        ResetArrangedChildren();
        double y = Border;
        double rowWidth = Math.Max(0, _gridSize.Width - 2 * Border);
        foreach (var row in Rows())
        {
            ArrangeBlockChild(row, new Rect(Border, y, rowWidth, row.DesiredSize.Height));
            y += row.DesiredSize.Height + Border;
        }

        Redraw();
    }

    protected override void DrawDelegatedChildren(CanvasDrawingSession session, Rect region)
    {
        // MdTable.DrawBlock draws every cell directly with table borders and column clipping.
    }

    protected override void DrawBlock(CanvasDrawingSession session, Rect region)
    {
        session.FillRectangle(new Rect(0, 0, _gridSize.Width, _gridSize.Height), Sheet.TableBorder);
        Matrix3x2 baseTransform = session.Transform;
        double y = Border;
        foreach (MdTableRow row in Rows())
        {
            double rowHeight = row.ActualHeight > 0 ? row.ActualHeight : row.DesiredSize.Height;
            if (y + rowHeight < region.Top - 2 || y > region.Bottom + 2)
            {
                y += rowHeight + Border;
                continue;
            }

            double x = Border;
            for (int i = 0; i < row.Blocks.Count; i++)
            {
                double column = i < _columns.Length ? _columns[i] : 0;
                if (row.Blocks[i] is MdTableCell cell && column > 0 && rowHeight > 0
                    && x + column >= region.Left - 2 && x <= region.Right + 2)
                {
                    var cellRect = new Rect(x, y, column, rowHeight);
                    session.FillRectangle(cellRect, cell.Context.Surface);

                    double clipLeft = Math.Max(region.Left, cellRect.Left) - x;
                    double clipTop = Math.Max(region.Top, cellRect.Top) - y;
                    double clipRight = Math.Min(region.Right, cellRect.Right) - x;
                    double clipBottom = Math.Min(region.Bottom, cellRect.Bottom) - y;
                    Rect localRegion = clipRight > clipLeft && clipBottom > clipTop
                        ? new Rect(clipLeft, clipTop, clipRight - clipLeft, clipBottom - clipTop)
                        : new Rect(0, 0, column, rowHeight);

                    bool overflows = cell.TextLayout is { } layout && layout.ContentWidth > layout.Width + 0.5;
                    CanvasActiveLayer? layer = overflows ? session.CreateLayer(1f, cellRect) : null;
                    try
                    {
                        session.Transform = Matrix3x2.CreateTranslation((float)x, (float)y) * baseTransform;
                        cell.DrawDelegated(session, localRegion);
                    }
                    finally
                    {
                        session.Transform = baseTransform;
                        layer?.Dispose();
                    }
                }

                x += column + Border;
            }

            y += rowHeight + Border;
        }
    }

    internal override void InvalidateDelegatedChild(MdBlockView child, Rect? childRegion)
    {
        if (_measuring)
        {
            return;
        }

        if (_gridSize.Width > 0 && _gridSize.Height > 0)
        {
            double y = Border;
            foreach (MdTableRow row in Rows())
            {
                double rowHeight = row.ActualHeight > 0 ? row.ActualHeight : row.DesiredSize.Height;
                double x = Border;
                for (int i = 0; i < row.Blocks.Count; i++)
                {
                    double column = i < _columns.Length ? _columns[i] : 0;
                    if (ReferenceEquals(row.Blocks[i], child))
                    {
                        if (column > 0 && rowHeight > 0)
                        {
                            double left = Math.Max(0, x - 1);
                            double top = Math.Max(0, y - 1);
                            double right = Math.Min(_gridSize.Width, x + column + 1);
                            double bottom = Math.Min(_gridSize.Height, y + rowHeight + 1);
                            if (right > left && bottom > top)
                            {
                                InvalidateRegion(new Rect(left, top, right - left, bottom - top));
                                return;
                            }
                        }

                        Redraw();
                        return;
                    }

                    x += column + Border;
                }

                y += rowHeight + Border;
            }
        }

        Redraw();
    }

    protected override void OnBlockPooled()
    {
        base.OnBlockPooled();
        _columns = [];
        _gridSize = default;
    }

    protected override void OnBlockUnmounted()
    {
        base.OnBlockUnmounted();
        _columns = [];
        _gridSize = default;
    }

    /// <summary>
    /// Natural widths when they fit; otherwise every column first gets up to an equal share, and the
    /// rest of the space goes to the columns that want more, in proportion to what they lack.
    /// </summary>
    private static double[] Fit(double[] natural, double available)
    {
        double total = natural.Sum();
        if (total <= available || natural.Length == 0)
        {
            return natural;
        }

        double share = Math.Max(MinimumColumn, available / natural.Length);
        var widths = natural.Select(width => Math.Min(width, share)).ToArray();
        double rest = Math.Max(0, available - widths.Sum());
        double lacking = natural.Select((width, i) => width - widths[i]).Sum();
        if (lacking > 0)
        {
            for (int i = 0; i < widths.Length; i++)
            {
                widths[i] += rest * (natural[i] - widths[i]) / lacking;
            }
        }

        return widths;
    }

    private IEnumerable<MdTableRow> Rows() => Blocks.OfType<MdTableRow>();

    private bool TryBuildNaturalWidthKey(out TableNaturalWidthKey key, out int columnCount)
    {
        int rowCount = 0;
        columnCount = 0;
        ulong hash = 14695981039346656037UL;
        foreach (var child in Blocks)
        {
            if (child is not MdTableRow row)
            {
                key = default;
                return false;
            }

            rowCount++;
            columnCount = Math.Max(columnCount, row.Blocks.Count);
            hash = Mix(hash, (ulong)(uint)row.Blocks.Count);
            for (int col = 0; col < row.Blocks.Count; col++)
            {
                if (row.Blocks[col] is not MdTableCell cell)
                {
                    key = default;
                    return false;
                }

                var content = cell.Content;
                foreach (var run in content.Runs)
                {
                    if (run.Role == RunRole.Image)
                    {
                        key = default;
                        return false;
                    }
                }

                foreach (var preview in content.Previews)
                {
                    if (preview.Role == RunRole.Image)
                    {
                        key = default;
                        return false;
                    }
                }

                hash = Mix(hash, (ulong)(uint)col);
                hash = Mix(hash, cell.IsHeader ? 1UL : 0UL);
                hash = Mix(hash, content.StructuralHash);
            }
        }

        key = new TableNaturalWidthKey(
            hash,
            rowCount,
            columnCount,
            Sheet.FontFamily,
            Sheet.MonospaceFamily,
            Sheet.FontSize,
            Sheet.BlockBoldWeight,
            Sheet.FootnoteBold,
            Sheet.CellPaddingHorizontal,
            Sheet.CellPaddingVertical,
            Sheet.CodeScale,
            Sheet.ScriptScale,
            Sheet.MathScale,
            Sheet.LineHeight,
            Sheet.IncrementalTabStop);
        return true;

        static ulong Mix(ulong state, ulong value)
        {
            state ^= value;
            return state * 1099511628211UL;
        }
    }

    private static bool TryGetCachedNaturalWidths(TableNaturalWidthKey key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out double[]? widths)
    {
        lock (NaturalWidthGate)
        {
            if (NaturalWidthCache.TryGetValue(key, out var node))
            {
                NaturalWidthOrder.Remove(node);
                NaturalWidthOrder.AddFirst(node);
                widths = (double[])node.Value.Widths.Clone();
                return true;
            }
        }

        widths = null;
        return false;
    }

    private static void RememberNaturalWidths(TableNaturalWidthKey key, double[] widths)
    {
        var copy = (double[])widths.Clone();
        lock (NaturalWidthGate)
        {
            if (NaturalWidthCache.TryGetValue(key, out var existing))
            {
                NaturalWidthOrder.Remove(existing);
                existing.Value = new NaturalWidthEntry(key, copy);
                NaturalWidthOrder.AddFirst(existing);
                return;
            }

            var node = NaturalWidthOrder.AddFirst(new NaturalWidthEntry(key, copy));
            NaturalWidthCache[key] = node;
            while (NaturalWidthOrder.Count > NaturalWidthCacheCapacity && NaturalWidthOrder.Last is { } oldest)
            {
                NaturalWidthOrder.RemoveLast();
                NaturalWidthCache.Remove(oldest.Value.Key);
            }
        }
    }

    private readonly record struct TableNaturalWidthKey(
        ulong ContentHash,
        int RowCount,
        int ColumnCount,
        string FontFamily,
        string MonospaceFamily,
        float FontSize,
        ushort BoldWeight,
        bool FootnoteBold,
        double CellPadH,
        double CellPadV,
        float CodeScale,
        float ScriptScale,
        float MathScale,
        float LineHeight,
        float IncrementalTabStop);

    private sealed record NaturalWidthEntry(TableNaturalWidthKey Key, double[] Widths);
}
