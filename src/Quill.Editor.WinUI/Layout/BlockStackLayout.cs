using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quill.VariableXaml.Model;
using Windows.Foundation;

namespace Quill.Editor.WinUI.Layout;

/// <summary>
/// The virtualizing layout of the top-level blocks (Q69): a vertical stack that remembers the
/// measured height of every block by its key, so blocks scrolled away keep their real height and the
/// extent and positions stay exact once a block has been seen. Blocks never measured get an estimate
/// from their content. Block tops come from prefix sums, so a height change or a lookup costs
/// O(log n); inserted and removed blocks update the sums in place (asking the repeater only for the
/// new items), and only a reset rebuilds them. Exact positions let the layout
/// service lay out blocks outside the element tree (Q71). The space after a block depends on it and
/// the next block (collapsed margins, <see cref="BlockMargins"/>); each prefix entry is a block's
/// height plus the gap below it, the last block having none.
/// </summary>
public sealed partial class BlockStackLayout : VirtualizingLayout
{
    private readonly Dictionary<string, (VNode Node, double Height)> _measured = new(StringComparer.Ordinal);
    private readonly Dictionary<int, UIElement> _realized = [];
    private readonly PrefixSums _tops = new();
    private readonly List<double> _gaps = [];
    private bool _stale = true;
    private double _width = double.NaN;
    private Func<VNode, VNode, double> _gap = static (_, _) => 0;

    /// <summary>Estimates the height of a block never measured: its node and the column width.</summary>
    public Func<VNode, double, double> Estimate { get; set; } = static (_, _) => 24;

    /// <summary>The space between two adjacent blocks, the upper first.</summary>
    public Func<VNode, VNode, double> Gap
    {
        get => _gap;
        set
        {
            _gap = value;
            _stale = true;
            InvalidateMeasure();
        }
    }

    /// <summary>The number of blocks laid out at the last measure.</summary>
    public int Count => _tops.Count;

    /// <summary>The column width of the last measure.</summary>
    public double Width => _width;

    /// <summary>The top of a block in the stack's coordinates.</summary>
    public double TopOf(int index) => _tops.Before(index);

    /// <summary>The height of a block: measured, or estimated if it never was.</summary>
    public double HeightOf(int index) => _tops[index] - _gaps[index];

    /// <summary>The block at a vertical offset in the stack's coordinates (the one above in a gap).</summary>
    public int IndexAt(double y) => _tops.IndexAt(y);

    /// <summary>Whether the block's height was measured at the current width (not estimated).</summary>
    public bool IsMeasured(int index, VNode node) => node.Key is { } key && _measured.TryGetValue(key, out var entry) && ReferenceEquals(entry.Node, node);

    protected override void InitializeForContextCore(VirtualizingLayoutContext context)
    {
        _realized.Clear();
        _stale = true;
    }

    protected override void UninitializeForContextCore(VirtualizingLayoutContext context) => _realized.Clear();

    protected override void OnItemsChangedCore(VirtualizingLayoutContext context, object source, NotifyCollectionChangedEventArgs args)
    {
        // The repeater recycles the elements of removed items; the others shift with their items.
        var shifted = new Dictionary<int, UIElement>();
        foreach (var (index, element) in _realized)
        {
            int moved = args.Action switch
            {
                NotifyCollectionChangedAction.Add when index >= args.NewStartingIndex => index + args.NewItems!.Count,
                NotifyCollectionChangedAction.Remove when index >= args.OldStartingIndex + args.OldItems!.Count => index - args.OldItems.Count,
                NotifyCollectionChangedAction.Remove when index >= args.OldStartingIndex => -1,
                NotifyCollectionChangedAction.Replace when index >= args.OldStartingIndex && index < args.OldStartingIndex + args.OldItems!.Count => -1,
                NotifyCollectionChangedAction.Reset or NotifyCollectionChangedAction.Move => -1,
                _ => index,
            };
            if (moved >= 0)
            {
                shifted[moved] = element;
            }
        }

        _realized.Clear();
        foreach (var (index, element) in shifted)
        {
            _realized[index] = element;
        }

        // Inserted and removed items change only their own heights: the others keep theirs, and
        // only the new items are asked for (each a call into the repeater), not every item (09).
        if (!_stale && !double.IsNaN(_width) && args.Action == NotifyCollectionChangedAction.Add && args.NewStartingIndex >= 0 && args.NewStartingIndex <= _tops.Count)
        {
            int start = args.NewStartingIndex;
            var heights = new double[args.NewItems!.Count];
            var gaps = new double[heights.Length];
            for (int i = 0; i < heights.Length; i++)
            {
                gaps[i] = GapAfter(context, start + i, context.ItemCount);
                heights[i] = HeightFor(context.GetItemAt(start + i) as VNode) + gaps[i];
            }

            _gaps.InsertRange(start, gaps);
            _tops.Insert(start, heights);
            RefreshGap(context, start - 1);
        }
        else if (!_stale && args.Action == NotifyCollectionChangedAction.Remove && args.OldStartingIndex >= 0 && args.OldStartingIndex + args.OldItems!.Count <= _tops.Count)
        {
            foreach (var removed in args.OldItems.OfType<VNode>())
            {
                if (removed.Key is { } key && _measured.TryGetValue(key, out var entry) && ReferenceEquals(entry.Node, removed))
                {
                    _measured.Remove(key);
                }
            }

            _gaps.RemoveRange(args.OldStartingIndex, args.OldItems.Count);
            _tops.RemoveRange(args.OldStartingIndex, args.OldItems.Count);
            RefreshGap(context, args.OldStartingIndex - 1);
        }
        else
        {
            _stale = true;
        }

        InvalidateMeasure();
    }

    protected override Size MeasureOverride(VirtualizingLayoutContext context, Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 800 : availableSize.Width;
        if (!width.Equals(_width))
        {
            // Measured heights at another width stay as estimates until the blocks are measured again.
            _width = width;
            _stale = true;
        }

        int count = context.ItemCount;
        if (_stale || _tops.Count != count)
        {
            Rebuild(context, count);
        }

        var window = context.RealizationRect;
        int first = count == 0 ? 0 : _tops.IndexAt(Math.Max(0, window.Y));

        // Recycle elements outside the expected window before instantiating new ones so the pool
        // serves the incoming viewport directly instead of peaking at 2x active elements.
        int estimatedLast = first;
        while (estimatedLast + 1 < count && _tops.Before(estimatedLast + 1) < window.Y + window.Height)
        {
            estimatedLast++;
        }

        foreach (int index in _realized.Keys.Where(index => index < first || index > estimatedLast).ToList())
        {
            context.RecycleElement(_realized[index]);
            _realized.Remove(index);
        }

        int last = first - 1;
        for (int index = first; index < count && (index == first || _tops.Before(index) < window.Y + window.Height); index++)
        {
            var element = context.GetOrCreateElementAt(index);
            element.Measure(new Size(width, double.PositiveInfinity));
            Record(context, index, element.DesiredSize.Height);
            _realized[index] = element;
            last = index;
        }

        foreach (int index in _realized.Keys.Where(index => index < first || index > last).ToList())
        {
            context.RecycleElement(_realized[index]);
            _realized.Remove(index);
        }

        return new Size(width, Math.Max(0, _tops.Total));
    }

    protected override Size ArrangeOverride(VirtualizingLayoutContext context, Size finalSize)
    {
        foreach (var (index, element) in _realized)
        {
            element.Arrange(new Rect(0, _tops.Before(index), finalSize.Width, HeightOf(index)));
        }

        return finalSize;
    }

    private void Record(VirtualizingLayoutContext context, int index, double height)
    {
        if (context.GetItemAt(index) is VNode { Key: { } key } node)
        {
            _measured[key] = (node, height);
        }

        _tops.Set(index, height + _gaps[index]);
    }

    /// <summary>The gap below a block: none below the last.</summary>
    private double GapAfter(VirtualizingLayoutContext context, int index, int count) =>
        index + 1 < count && context.GetItemAt(index) is VNode upper && context.GetItemAt(index + 1) is VNode lower ? _gap(upper, lower) : 0;

    /// <summary>Takes the gap below a block again: its neighbor below changed.</summary>
    private void RefreshGap(VirtualizingLayoutContext context, int index)
    {
        if (index < 0 || index >= _gaps.Count)
        {
            return;
        }

        double height = _tops[index] - _gaps[index];
        _gaps[index] = GapAfter(context, index, _gaps.Count);
        _tops.Set(index, height + _gaps[index]);
    }

    /// <summary>The measured height of a block at the current width, or its estimate.</summary>
    private double HeightFor(VNode? node) =>
        node?.Key is { } key && _measured.TryGetValue(key, out var entry) ? entry.Height : node is null ? 24 : Estimate(node, _width);

    private void Rebuild(VirtualizingLayoutContext context, int count)
    {
        var heights = new double[count];
        _gaps.Clear();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < count; i++)
        {
            var node = context.GetItemAt(i) as VNode;
            double height = HeightFor(node);
            if (node?.Key is { } seen)
            {
                keys.Add(seen);
            }

            _gaps.Add(GapAfter(context, i, count));
            heights[i] = height + _gaps[i];
        }

        // Forget blocks that are gone.
        foreach (string key in _measured.Keys.Where(key => !keys.Contains(key)).ToList())
        {
            _measured.Remove(key);
        }

        _tops.Reset(heights);
        _stale = false;
    }

    internal void ClearCache()
    {
        _measured.Clear();
        _realized.Clear();
        _gaps.Clear();
        _stale = true;
    }
}
