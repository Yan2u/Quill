using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Quill.Playground.VariableXaml.Markup;
using Quill.Playground.VariableXaml.Metadata;
using Quill.Playground.VariableXaml.Model;
using Quill.Playground.VariableXaml.Virtualization;

namespace Quill.Playground.VariableXaml.Reconciliation;

/// <summary>
/// Applies virtual trees to live WinUI objects with minimal mutations.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Unchanged subtrees are skipped in O(1): by reference, else by 64-bit structural hash.</item>
/// <item>Same type and key: attributes are diffed and only changed members are set or cleared;
/// property elements and content are reconciled recursively.</item>
/// <item>Child lists use keys (<c>vx:Key</c>/<c>x:Name</c>), then content hashes, then type order
/// to match old and new children; the minimal set of moves is derived from a longest increasing
/// subsequence and applied with UIElementCollection.Move where available (keeps focus).</item>
/// <item>New subtrees are created with one XamlReader.Load of serialized markup, then bound back
/// to their virtual nodes; the reflection-based object model is the fallback path.</item>
/// </list>
/// All methods must run on the UI thread.
/// </remarks>
internal sealed partial class Reconciler
{
    private static readonly SolidColorBrush PlaceholderBrush = new(Colors.IndianRed);

    private readonly XamlSchema _schema;
    private readonly XamlMarkupWriter _writer;
    private readonly ValueConverter _converter = new();
    private readonly MarkupEventBinder _eventBinder;
    private readonly FrameworkElement _host;
    private readonly Dictionary<XamlName, IVirtualElementHandler> _virtualHandlers = [];
    private readonly HashSet<VirtualStackState> _virtualStates = [];
    private List<string> _diagnostics = [];
    private List<string> _trace = [];

    public Reconciler(FrameworkElement host, XamlSchema schema, Action<string, object?, object?> eventDispatch)
    {
        _host = host;
        _schema = schema;
        _writer = new XamlMarkupWriter(schema);
        _eventBinder = new MarkupEventBinder(eventDispatch);
        _virtualHandlers[XamlName.VariableXaml("VirtualStack")] = new VirtualStackHandler();
    }

    public XamlSchema Schema => _schema;

    /// <summary>
    /// Counters of the update in progress. Work done outside an update (for example items
    /// realized by a virtual stack while scrolling) lands in a fresh object after EndUpdate.
    /// </summary>
    public UpdateStatistics Statistics { get; private set; } = new();

    public IReadOnlyList<string> Diagnostics => _diagnostics;

    /// <summary>Virtual stacks currently mounted (for diagnostics).</summary>
    public IReadOnlyCollection<VirtualStackState> VirtualStates => _virtualStates;

    /// <summary>Create new subtrees with XamlReader (true) or only through the object model (false).</summary>
    public bool MountWithXamlReader { get; set; } = true;

    public void BeginUpdate()
    {
        Statistics = new UpdateStatistics();
        _diagnostics = [];
        _trace = [];
    }

    /// <summary>Finishes an update and hands its counters, diagnostics and trace to the caller.</summary>
    public (UpdateStatistics Statistics, IReadOnlyList<string> Diagnostics, IReadOnlyList<string> Trace) EndUpdate()
    {
        var result = (Statistics, (IReadOnlyList<string>)_diagnostics, (IReadOnlyList<string>)_trace);
        Statistics = new UpdateStatistics();
        _diagnostics = [];
        _trace = [];
        return result;
    }

    internal void RegisterVirtualState(VirtualStackState state) => _virtualStates.Add(state);

    internal void UnregisterVirtualState(VirtualStackState state) => _virtualStates.Remove(state);

    public LiveNode MountRoot(VNode node) => Mount(node, null, typeof(object));

    /// <summary>Patches the root; the returned node differs from <paramref name="root"/> when it was replaced.</summary>
    public LiveNode PatchRoot(LiveNode root, VNode node) => Patch(root, node, typeof(object));

    public void UnmountRoot(LiveNode root) => Unmount(root);

    internal void Report(string message)
    {
        if (_diagnostics.Count < 100)
        {
            _diagnostics.Add(message);
        }
    }

    /// <summary>
    /// Brings <paramref name="live"/> up to date with <paramref name="next"/>. Returns the same node
    /// when patched in place, or a newly mounted node that the caller must put into the slot.
    /// </summary>
    /// <param name="slotType">The item type the parent slot accepts (UIElement, Inline, object...).</param>
    /// <param name="ignoreKey">Patch across keys (used when a recycled element is repurposed).</param>
    internal LiveNode Patch(LiveNode live, VNode next, Type slotType, bool ignoreKey = false)
    {
        if (ReferenceEquals(live.Node, next))
        {
            Statistics.SubtreesSkipped++;
            return live;
        }

        if (live.Node.Hash == next.Hash && !live.IsPlaceholder)
        {
            live.Node = next;
            Statistics.SubtreesSkipped++;
            return live;
        }

        if (!CanPatch(live, next, ignoreKey))
        {
            Trace($"replaced {Describe(live.Node)} with {Describe(next)}: {ReplacementReason(live, next)}");
            LiveNode replacement = Mount(next, live.Parent, slotType);
            Unmount(live);
            Statistics.NodesReplaced++;
            return replacement;
        }

        Statistics.NodesPatched++;
        if (next.IsText)
        {
            return PatchText(live, next);
        }

        if (live.VirtualState is not null)
        {
            _virtualHandlers[next.Type].Patch(this, live, next);
            live.Node = next;
            return live;
        }

        VNode previous = live.Node;
        PatchAttributes(live, previous, next);
        PatchPropertyElements(live, previous, next);
        PatchContent(live, previous, next);
        live.Node = next;
        return live;
    }

    internal void Unmount(LiveNode live)
    {
        Statistics.NodesRemoved++;
        if (live.VirtualState is not null)
        {
            _virtualHandlers[live.Node.Type].Unmount(this, live);
            live.VirtualState = null;
        }

        if (live.Events is not null)
        {
            foreach ((XamlMemberName name, Delegate handler) in live.Events)
            {
                live.Type?.ResolveMember(name)?.Event?.RemoveEventHandler(live.Instance, handler);
            }

            live.Events = null;
        }

        if (live.Children is not null)
        {
            foreach (LiveNode child in live.Children)
            {
                Unmount(child);
            }
        }

        if (live.Properties is not null)
        {
            foreach (LiveProperty property in live.Properties.Values)
            {
                property.Items?.ForEach(Unmount);
                if (property.Value is not null)
                {
                    Unmount(property.Value);
                }
            }
        }
    }

    internal bool IsVirtualElement(VNode node) => !node.IsText && _virtualHandlers.ContainsKey(node.Type);

    /// <summary>Records an informational event of the current update (not a problem).</summary>
    internal void Trace(string message)
    {
        if (_trace.Count < 50)
        {
            _trace.Add(message);
        }
    }

    private static string Describe(VNode node) => node.IsText ? "text" : node.Key is null ? $"<{node.Type.LocalName}>" : $"<{node.Type.LocalName} key={node.Key}>";

    private static string ReplacementReason(LiveNode live, VNode next) => live switch
    {
        { IsPlaceholder: true } => "retrying an element that previously failed",
        _ when live.Node.Kind != next.Kind => "text and element swapped",
        _ when live.Node.Type != next.Type => "element type changed",
        _ when live.Node.Key != next.Key => "key changed",
        { Type.IsOpaque: true } => "opaque content (template/style/resources) changed",
        _ => "not patchable",
    };

    private static bool CanPatch(LiveNode live, VNode next, bool ignoreKey = false)
    {
        if (live.IsPlaceholder || live.Node.Kind != next.Kind)
        {
            return false;
        }

        if (next.IsText)
        {
            return true;
        }

        return live.Node.Type == next.Type && (ignoreKey || live.Node.Key == next.Key) && live.Type?.IsOpaque != true;
    }

    private LiveNode PatchText(LiveNode live, VNode next)
    {
        Statistics.TextUpdates++;
        switch (live.Instance)
        {
            case Run run:
                run.Text = next.Text;
                live.Node = next;
                return live;
            case TextBlock block:
                block.Text = next.Text;
                live.Node = next;
                return live;
            default:
                // Strings are immutable: hand back a new node; the caller swaps it into the slot.
                return new LiveNode(next, next.Text, null, live.Parent);
        }
    }

    /// <summary>
    /// Creates the stand-in for a node that cannot be created, matched to what the slot accepts.
    /// In slots that can display nothing (e.g. a brush property) the instance is null and the
    /// caller leaves the member untouched.
    /// </summary>
    private LiveNode CreatePlaceholder(VNode node, LiveNode? parent, Type slotType, string message)
    {
        Report(message);
        object? instance = null;
        if (slotType.IsAssignableFrom(typeof(Border)))
        {
            instance = new Border
            {
                BorderBrush = PlaceholderBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6, 2, 6, 2),
                Child = new TextBlock { Text = message, Foreground = PlaceholderBrush, TextWrapping = TextWrapping.Wrap },
            };
        }
        else if (slotType.IsAssignableFrom(typeof(Run)))
        {
            instance = new Run { Text = $"[{message}]", Foreground = PlaceholderBrush };
        }

        return new LiveNode(node, instance, null, parent) { IsPlaceholder = true };
    }

    /// <summary>
    /// Filters out children the slot can never hold (reported as diagnostics), so live
    /// collections stay index-aligned with the virtual children.
    /// </summary>
    private VNode[] PrepareChildren(VNode[] children, Type slotType, VNode owner)
    {
        List<VNode>? kept = null;
        for (int i = 0; i < children.Length; i++)
        {
            if (CanOccupySlot(children[i], slotType, owner))
            {
                kept?.Add(children[i]);
            }
            else
            {
                kept ??= new List<VNode>(children[..i]);
            }
        }

        return kept?.ToArray() ?? children;
    }

    private bool CanOccupySlot(VNode child, Type slotType, VNode owner)
    {
        bool canShowPlaceholder = slotType.IsAssignableFrom(typeof(Border)) || slotType.IsAssignableFrom(typeof(Run));
        if (child.IsText)
        {
            if (slotType.IsAssignableFrom(typeof(Run)) || slotType == typeof(object) || slotType.IsAssignableFrom(typeof(TextBlock)))
            {
                return true;
            }

            Report($"Text \"{Truncate(child.Text!)}\" is not allowed inside <{owner.Type.LocalName}>.");
            return false;
        }

        if (IsVirtualElement(child) || canShowPlaceholder)
        {
            return true;
        }

        XamlTypeInfo? type = _schema.GetType(child.Type);
        if (type is not null && slotType.IsAssignableFrom(type.ClrType))
        {
            return true;
        }

        Report(type is null
            ? $"Unknown element <{child.Type.LocalName}> inside <{owner.Type.LocalName}>."
            : $"<{child.Type.LocalName}> cannot be placed inside <{owner.Type.LocalName}> (expects {slotType.Name}).");
        return false;
    }

    private static string Truncate(string text) => text.Length <= 24 ? text : text[..24] + "...";
}
