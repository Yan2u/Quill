using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quill.VariableXaml.Model;
using Quill.VariableXaml.Reconciliation;

namespace Quill.VariableXaml.Virtualization;

/// <summary>
/// Live state of one <c>vx:VirtualStack</c>: an ItemsRepeater + StackLayout whose items are the
/// virtual child nodes. Only items near the viewport are materialized; the reconciler is the
/// element factory, so a recycled element is patched into the next item of the same type.
/// </summary>
/// <remarks>
/// Recycled elements stay children of the ItemsRepeater (like RecyclePool with an owner), which
/// avoids leave/enter-tree costs. Placeholders are never pooled; they are removed instead.
/// The element → LiveNode link is stored in an attached property rather than a dictionary keyed
/// by element: CsWinRT may hand back a different managed wrapper for the same native element.
/// </remarks>
internal sealed partial class VirtualStackState : IVirtualElementState
{
    private static readonly DependencyProperty LiveNodeProperty = DependencyProperty.RegisterAttached(
        "LiveNode", typeof(object), typeof(VirtualStackState), new PropertyMetadata(null));

    private const int MaxPooledPerType = 32;

    private readonly Reconciler _reconciler;
    private readonly ElementFactoryProxy _factory;
    private readonly Dictionary<string, Stack<LiveNode>> _pool = new(StringComparer.Ordinal);
    private readonly HashSet<LiveNode> _realized = [];

    public VirtualStackState(Reconciler reconciler)
    {
        _reconciler = reconciler;
        _factory = new ElementFactoryProxy(this);
        Layout = reconciler.VirtualLayoutFactory?.Invoke() ?? new StackLayout();
        Repeater = new ItemsRepeater { Layout = Layout, ItemsSource = Items, ItemTemplate = _factory };
    }

    public ItemsRepeater Repeater { get; }

    /// <summary>A StackLayout unless the host supplies another layout (<see cref="VariableXamlRoot.VirtualLayoutFactory"/>).</summary>
    public VirtualizingLayout Layout { get; }

    public VirtualItemCollection Items { get; } = [];

    /// <summary>The LiveNode representing the VirtualStack itself (parent of realized items).</summary>
    public LiveNode? Owner { get; set; }

    public int RealizedCount => _realized.Count;

    /// <summary>Elements created from scratch since mount.</summary>
    public int MountedTotal { get; private set; }

    /// <summary>Elements reused from the recycle pool (patched to a new item) since mount.</summary>
    public int ReusedTotal { get; private set; }

    public UIElement GetElement(ElementFactoryGetArgs args)
    {
        var node = (VNode)args.Data;
        if (_pool.TryGetValue(PoolKey(node), out Stack<LiveNode>? stack) && stack.TryPop(out LiveNode? pooled))
        {
            // Same type, not a placeholder: repurposing patches in place and never replaces.
            LiveNode patched = _reconciler.Patch(pooled, node, typeof(UIElement), ignoreKey: true);
            ReusedTotal++;
            return Register(patched);
        }

        MountedTotal++;
        return Register(_reconciler.Mount(node, Owner, typeof(UIElement)));
    }

    public void RecycleElement(ElementFactoryRecycleArgs args)
    {
        UIElement element = args.Element;
        if (element.GetValue(LiveNodeProperty) is not LiveNode live || !_realized.Remove(live))
        {
            return;
        }

        if (live.IsPlaceholder)
        {
            element.ClearValue(LiveNodeProperty);
            (args.Parent as Panel)?.Children.Remove(element);
            _reconciler.Unmount(live);
            return;
        }

        string key = PoolKey(live.Node);
        if (!_pool.TryGetValue(key, out Stack<LiveNode>? stack))
        {
            _pool[key] = stack = new Stack<LiveNode>();
        }

        if (stack.Count >= MaxPooledPerType)
        {
            element.ClearValue(LiveNodeProperty);
            (args.Parent as Panel)?.Children.Remove(element);
            _reconciler.Unmount(live);
            return;
        }

        NotifyPooled(live);
        stack.Push(live);
    }

    private static void NotifyPooled(LiveNode live)
    {
        (live.Instance as IVirtualLifecycle)?.OnPooled();
        if (live.Children is not null)
        {
            foreach (LiveNode child in live.Children)
            {
                NotifyPooled(child);
            }
        }

        if (live.Properties is not null)
        {
            foreach (LiveProperty property in live.Properties.Values)
            {
                if (property.Items is not null)
                {
                    foreach (LiveNode item in property.Items)
                    {
                        NotifyPooled(item);
                    }
                }

                if (property.Value is not null)
                {
                    NotifyPooled(property.Value);
                }
            }
        }
    }

    /// <summary>Returns the realized live node for an item index, or null if the item is virtualized.</summary>
    public LiveNode? TryGetRealized(int index) =>
        Repeater.TryGetElement(index)?.GetValue(LiveNodeProperty) is LiveNode live && _realized.Contains(live) ? live : null;

    public void Release()
    {
        foreach (LiveNode live in _realized.Concat(_pool.Values.SelectMany(static stack => stack)))
        {
            if (live.Instance is UIElement element)
            {
                element.ClearValue(LiveNodeProperty);
            }

            _reconciler.Unmount(live);
        }

        _realized.Clear();
        _pool.Clear();
        Owner = null;
        _factory.Detach();
        Repeater.ItemsSource = null;
        Items.ResetTo([]);
    }

    private UIElement Register(LiveNode live)
    {
        var element = (UIElement)live.Instance!;
        element.SetValue(LiveNodeProperty, live);
        _realized.Add(live);
        return element;
    }

    private static string PoolKey(VNode node) => node.IsText ? "#text" : node.Type.ToString();

    private sealed partial class ElementFactoryProxy(VirtualStackState state) : IElementFactory
    {
        private VirtualStackState? _state = state;

        public void Detach() => _state = null;

        public UIElement GetElement(ElementFactoryGetArgs args) => _state?.GetElement(args) ?? new Border();

        public void RecycleElement(ElementFactoryRecycleArgs args) => _state?.RecycleElement(args);
    }
}
