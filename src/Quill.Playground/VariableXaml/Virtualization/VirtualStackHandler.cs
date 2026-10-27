using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Quill.Playground.VariableXaml.Model;
using Quill.Playground.VariableXaml.Reconciliation;

namespace Quill.Playground.VariableXaml.Virtualization;

/// <summary>
/// <c>&lt;vx:VirtualStack Spacing="8"&gt;...&lt;/vx:VirtualStack&gt;</c>: a vertically stacked list whose
/// children are virtualized with ItemsRepeater. Place it inside a ScrollViewer.
/// </summary>
/// <remarks>
/// <c>Spacing</c> and <c>Orientation</c> configure the StackLayout; all other attributes apply to
/// the ItemsRepeater (Margin, HorizontalAlignment, VerticalCacheLength...).
/// Children are diffed like any child list, but the operations target the items source:
/// structural changes raise collection notifications, while content changes patch the realized
/// element in place (or nothing at all when the item is not realized).
/// </remarks>
internal sealed class VirtualStackHandler : IVirtualElementHandler
{
    private static readonly XamlName RepeaterType = XamlName.Presentation(nameof(ItemsRepeater));

    public LiveNode Mount(Reconciler reconciler, VNode node, LiveNode? parent)
    {
        var state = new VirtualStackState(reconciler);
        var live = new LiveNode(node, state.Repeater, reconciler.Schema.GetType(RepeaterType), parent) { VirtualState = state };
        state.Owner = live;
        reconciler.Statistics.NodesCreated++;

        foreach (VAttribute attribute in node.AttributesArray)
        {
            ApplyAttribute(reconciler, live, state, attribute);
        }

        state.Items.ResetTo(node.ChildrenArray);
        reconciler.RegisterVirtualState(state);
        return live;
    }

    public void Patch(Reconciler reconciler, LiveNode live, VNode next)
    {
        var state = (VirtualStackState)live.VirtualState!;
        VNode previous = live.Node;
        foreach (VAttribute attribute in next.AttributesArray)
        {
            int index = Array.FindIndex(previous.AttributesArray, candidate => candidate.Name == attribute.Name);
            if (index < 0 || previous.AttributesArray[index] != attribute)
            {
                ApplyAttribute(reconciler, live, state, attribute);
            }
        }

        foreach (VAttribute attribute in previous.AttributesArray)
        {
            if (!Array.Exists(next.AttributesArray, candidate => candidate.Name == attribute.Name))
            {
                ClearAttribute(reconciler, live, state, attribute);
            }
        }

        UpdateItems(reconciler, state, next.ChildrenArray);
    }

    public void Unmount(Reconciler reconciler, LiveNode live)
    {
        var state = (VirtualStackState)live.VirtualState!;
        reconciler.UnregisterVirtualState(state);
        state.Release();
    }

    private static void UpdateItems(Reconciler reconciler, VirtualStackState state, VNode[] next)
    {
        VirtualItemCollection items = state.Items;
        int oldCount = items.Count;
        int newCount = next.Length;
        if (oldCount == 0 || newCount == 0)
        {
            items.ResetTo(next);
            return;
        }

        int start = 0;
        while (start < oldCount && start < newCount && ChildMatcher.IsSameIdentity(items[start], next[start]))
        {
            UpdateItem(reconciler, state, start, next[start]);
            start++;
        }

        int oldEnd = oldCount - 1;
        int newEnd = newCount - 1;
        while (oldEnd >= start && newEnd >= start && ChildMatcher.IsSameIdentity(items[oldEnd], next[newEnd]))
        {
            UpdateItem(reconciler, state, oldEnd, next[newEnd]);
            oldEnd--;
            newEnd--;
        }

        if (start > oldEnd)
        {
            for (int j = start; j <= newEnd; j++)
            {
                items.Insert(j, next[j]);
            }

            return;
        }

        if (start > newEnd)
        {
            for (int i = oldEnd; i >= start; i--)
            {
                items.RemoveAt(i);
            }

            return;
        }

        int oldLength = oldEnd - start + 1;
        int newLength = newEnd - start + 1;
        var oldNodes = new VNode[oldLength];
        for (int i = 0; i < oldLength; i++)
        {
            oldNodes[i] = items[start + i];
        }

        int[] sources = ChildMatcher.Match(oldNodes, _ => true, new ArraySegment<VNode>(next, start, newLength));
        bool[] used = new bool[oldLength];
        foreach (int source in sources)
        {
            if (source >= 0)
            {
                used[source] = true;
            }
        }

        // Too much churn: one Reset is cheaper than many notifications (the pool still reuses elements).
        int unmatched = sources.Count(source => source < 0) + used.Count(isUsed => !isUsed);
        if (unmatched > Math.Max(32, (oldCount + newCount) / 4))
        {
            items.ResetTo(next);
            return;
        }

        for (int i = oldLength - 1; i >= 0; i--)
        {
            if (!used[i])
            {
                items.RemoveAt(start + i);
            }
        }

        // Mirror of old relative indices currently in the middle segment.
        var mirror = new List<int>(newLength);
        for (int i = 0; i < oldLength; i++)
        {
            if (used[i])
            {
                mirror.Add(i);
            }
        }

        bool[] stable = ChildMatcher.LongestIncreasingSubsequence(sources);
        int anchor = mirror.Count;
        for (int j = newLength - 1; j >= 0; j--)
        {
            if (sources[j] < 0)
            {
                items.Insert(start + anchor, next[start + j]);
                mirror.Insert(anchor, -1);
                continue;
            }

            int current = mirror.IndexOf(sources[j]);
            if (!stable[j])
            {
                int target = current < anchor ? anchor - 1 : anchor;
                if (target != current)
                {
                    items.Move(start + current, start + target);
                    mirror.RemoveAt(current);
                    mirror.Insert(target, sources[j]);
                    reconciler.Statistics.NodesMoved++;
                    current = target;
                }
            }

            anchor = current;
        }

        for (int j = 0; j < newLength; j++)
        {
            if (sources[j] >= 0)
            {
                UpdateItem(reconciler, state, start + j, next[start + j]);
            }
        }
    }

    /// <summary>
    /// Stores the new node for a matched item without a collection notification, and patches
    /// the element only if it is currently realized.
    /// </summary>
    private static void UpdateItem(Reconciler reconciler, VirtualStackState state, int index, VNode next)
    {
        VNode current = state.Items[index];
        if (ReferenceEquals(current, next))
        {
            return;
        }

        state.Items.ReplaceSilently(index, next);
        if (current.Hash == next.Hash)
        {
            return;
        }

        LiveNode? realized = state.TryGetRealized(index);
        if (realized is null)
        {
            reconciler.Statistics.SubtreesSkipped++;
            return;
        }

        LiveNode patched = reconciler.Patch(realized, next, typeof(UIElement), ignoreKey: true);
        if (!ReferenceEquals(patched, realized))
        {
            // The element could not be patched (e.g. it was a placeholder): let the repeater swap it.
            reconciler.Unmount(patched);
            state.Items[index] = next;
        }
    }

    private static void ApplyAttribute(Reconciler reconciler, LiveNode live, VirtualStackState state, VAttribute attribute)
    {
        switch (attribute.Name.Member)
        {
            case nameof(StackLayout.Spacing) when attribute.Name.Kind == XamlMemberKind.Plain:
                state.Layout.Spacing = attribute.Value is string spacing ? (double)XamlBindingHelper.ConvertValue(typeof(double), spacing) : Convert.ToDouble(attribute.Value);
                reconciler.Statistics.PropertiesSet++;
                break;
            case nameof(StackLayout.Orientation) when attribute.Name.Kind == XamlMemberKind.Plain:
                state.Layout.Orientation = attribute.Value is string orientation ? Enum.Parse<Orientation>(orientation) : (Orientation)attribute.Value!;
                reconciler.Statistics.PropertiesSet++;
                break;
            default:
                reconciler.ApplyAttribute(live, attribute);
                break;
        }
    }

    private static void ClearAttribute(Reconciler reconciler, LiveNode live, VirtualStackState state, VAttribute attribute)
    {
        switch (attribute.Name.Member)
        {
            case nameof(StackLayout.Spacing) when attribute.Name.Kind == XamlMemberKind.Plain:
                state.Layout.ClearValue(StackLayout.SpacingProperty);
                break;
            case nameof(StackLayout.Orientation) when attribute.Name.Kind == XamlMemberKind.Plain:
                state.Layout.ClearValue(StackLayout.OrientationProperty);
                break;
            default:
                reconciler.ClearAttribute(live, attribute);
                break;
        }
    }
}
