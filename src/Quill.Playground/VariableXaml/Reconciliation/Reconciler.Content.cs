using Quill.Playground.VariableXaml.Metadata;
using Quill.Playground.VariableXaml.Model;

namespace Quill.Playground.VariableXaml.Reconciliation;

/// <summary>Content: property elements, single children and keyed child-list reconciliation.</summary>
internal sealed partial class Reconciler
{
    private void PatchPropertyElements(LiveNode live, VNode previous, VNode next)
    {
        VPropertyElement[] oldElements = previous.PropertyElementsArray;
        VPropertyElement[] newElements = next.PropertyElementsArray;
        if (oldElements.Length == 0 && newElements.Length == 0)
        {
            return;
        }

        foreach (VPropertyElement element in newElements)
        {
            VPropertyElement? old = Array.Find(oldElements, candidate => candidate.Member == element.Member);
            LiveProperty? property = null;
            if (live.Properties?.TryGetValue(element.Member, out property) != true)
            {
                MountPropertyElement(live, element);
            }
            else if (old?.Hash == element.Hash)
            {
                property!.Element = element;
            }
            else
            {
                PatchPropertyElement(live, property!, element);
            }
        }

        foreach (VPropertyElement old in oldElements)
        {
            if (Array.Exists(newElements, candidate => candidate.Member == old.Member))
            {
                continue;
            }

            if (live.Properties is not null && live.Properties.Remove(old.Member, out LiveProperty? property))
            {
                ReleasePropertyElement(property);
                try
                {
                    if (property.Collection is not null)
                    {
                        property.Collection.Clear();
                    }
                    else
                    {
                        property.Member.ClearValue(live.Instance!);
                    }

                    Statistics.PropertiesCleared++;
                }
                catch (Exception exception)
                {
                    Report($"Clearing {old.Member} failed: {FirstLine(exception.Message)}");
                }
            }
        }
    }

    private void PatchPropertyElement(LiveNode live, LiveProperty property, VPropertyElement element)
    {
        XamlMemberInfo member = property.Member;
        try
        {
            if (member.Category == XamlMemberCategory.Collection && property.Collection is not null)
            {
                Type slotType = member.CollectionItemType!;
                property.Items = ReconcileList(live, property.Collection, property.Items, PrepareChildren(element.ValuesArray, slotType, live.Node), slotType);
                property.Element = element;
                return;
            }

            if (member.Category == XamlMemberCategory.Property && property.Value is not null && element.ValuesArray is [{ IsText: false } value])
            {
                LiveNode patched = Patch(property.Value, value, member.ValueType);
                if (!ReferenceEquals(patched, property.Value) && patched.Instance is not null)
                {
                    member.SetValue(live.Instance!, patched.Instance);
                }

                property.Value = patched;
                property.Element = element;
                return;
            }
        }
        catch (Exception exception)
        {
            Report($"Updating {element.Member} failed: {FirstLine(exception.Message)}");
            return;
        }

        // Text values, dictionaries and value-shape changes: recreate the whole value.
        ReleasePropertyElement(property);
        property.Collection?.Clear();
        SetPropertyElementValue(live, property, element);
    }

    private void ReleasePropertyElement(LiveProperty property)
    {
        property.Items?.ForEach(Unmount);
        property.Items = null;
        if (property.Value is not null)
        {
            Unmount(property.Value);
            property.Value = null;
        }
    }

    private void PatchContent(LiveNode live, VNode previous, VNode next)
    {
        XamlTypeInfo type = live.Type!;
        object instance = live.Instance!;
        switch (type.ContentKind)
        {
            case XamlContentKind.Collection:
            {
                // The reconciler owns the content collection only while the node has children:
                // otherwise a text member such as TextBlock.Text may own it (Text fills Inlines).
                Type slotType = type.ContentMember!.CollectionItemType!;
                VNode[] children = PrepareChildren(next.ChildrenArray, slotType, next);
                List<LiveNode>? old = live.Children;
                bool ownedBefore = old is { Count: > 0 };
                if (children.Length == 0)
                {
                    if (ownedBefore)
                    {
                        // A text member applied by this patch has already replaced the items.
                        if (type.ShadowingTextMember is not { } textMember || next.GetAttribute(XamlMemberName.Plain(textMember)) is null)
                        {
                            (live.ChildCollection ??= ChildCollection.Wrap(type.ContentMember.GetValue(instance))!).Clear();
                        }

                        old!.ForEach(Unmount);
                        live.Children = null;
                    }

                    return;
                }

                IChildCollection collection = live.ChildCollection ??= ChildCollection.Wrap(type.ContentMember.GetValue(instance))!;
                if (!ownedBefore && collection.Count > 0)
                {
                    // Taking ownership: drop items a text member left behind (e.g. the Run of TextBlock.Text).
                    collection.Clear();
                }

                live.Children = ReconcileList(live, collection, old, children, slotType);
                return;
            }

            case XamlContentKind.SingleObject:
            {
                XamlMemberInfo member = type.ContentMember!;
                VNode[] children = PrepareChildren(next.ChildrenArray, member.ValueType, next);
                LiveNode? current = live.Children is [var only] ? only : null;
                if (children.Length == 0)
                {
                    if (current is not null)
                    {
                        member.ClearValue(instance);
                        Unmount(current);
                        live.Children = null;
                    }

                    return;
                }

                LiveNode result = current is null ? Mount(children[0], live, member.ValueType) : Patch(current, children[0], member.ValueType);
                if (!ReferenceEquals(result, current) && result.Instance is not null)
                {
                    member.SetValue(instance, result.Instance);
                }

                live.Children = [result];
                return;
            }

            case XamlContentKind.Text:
            {
                string text = JoinText(next.ChildrenArray);
                if (text != JoinText(previous.ChildrenArray))
                {
                    type.ContentMember!.SetValue(instance, text);
                    Statistics.TextUpdates++;
                }

                return;
            }
        }
    }

    /// <summary>
    /// Reconciles a live collection with a new list of virtual children and returns the new
    /// live child list (aligned with the collection).
    /// </summary>
    /// <remarks>
    /// 1. Common prefix and suffix are patched in place (identity = same key, or equal hash).
    /// 2. In the remaining middle, new children are matched to old ones by key, then by content
    ///    hash, then (unkeyed only) by type in document order, so an edited paragraph keeps its
    ///    element while an inserted one does not shift every following sibling.
    /// 3. Unmatched old children are removed; matched ones on the longest increasing subsequence
    ///    of old positions stay put, the others are moved; unmatched new children are mounted.
    /// </remarks>
    private List<LiveNode>? ReconcileList(LiveNode parent, IChildCollection collection, List<LiveNode>? oldList, VNode[] next, Type slotType)
    {
        List<LiveNode> old = oldList ?? [];
        int oldCount = old.Count;
        int newCount = next.Length;
        if (newCount == 0)
        {
            if (oldCount > 0)
            {
                collection.Clear();
                old.ForEach(Unmount);
            }

            return null;
        }

        var result = new LiveNode[newCount];
        int start = 0;
        while (start < oldCount && start < newCount && IsSameNode(old[start], next[start]))
        {
            result[start] = PatchAt(collection, start, old[start], next[start], slotType);
            start++;
        }

        int oldEnd = oldCount - 1;
        int newEnd = newCount - 1;
        while (oldEnd >= start && newEnd >= start && IsSameNode(old[oldEnd], next[newEnd]))
        {
            result[newEnd] = PatchAt(collection, oldEnd, old[oldEnd], next[newEnd], slotType);
            oldEnd--;
            newEnd--;
        }

        if (start > oldEnd)
        {
            for (int j = start; j <= newEnd; j++)
            {
                LiveNode mounted = Mount(next[j], parent, slotType);
                collection.Insert(j, mounted.Instance);
                result[j] = mounted;
            }
        }
        else if (start > newEnd)
        {
            for (int i = oldEnd; i >= start; i--)
            {
                collection.RemoveAt(i);
                Unmount(old[i]);
            }
        }
        else
        {
            ReconcileMiddle(parent, collection, old, start, oldEnd, next, newEnd, result, slotType);
        }

        return [.. result];
    }

    private void ReconcileMiddle(LiveNode parent, IChildCollection collection, List<LiveNode> old, int start, int oldEnd, VNode[] next, int newEnd, LiveNode[] result, Type slotType)
    {
        int oldLength = oldEnd - start + 1;
        int newLength = newEnd - start + 1;
        var oldNodes = new VNode[oldLength];
        for (int i = 0; i < oldLength; i++)
        {
            oldNodes[i] = old[start + i].Node;
        }

        int[] sources = ChildMatcher.Match(oldNodes, i => !old[start + i].IsPlaceholder, new ArraySegment<VNode>(next, start, newLength));
        bool[] used = new bool[oldLength];
        foreach (int source in sources)
        {
            if (source >= 0)
            {
                used[source] = true;
            }
        }

        // Remove unmatched old nodes (from the end so indices stay valid).
        for (int i = oldLength - 1; i >= 0; i--)
        {
            if (!used[i])
            {
                collection.RemoveAt(start + i);
                Unmount(old[start + i]);
            }
        }

        var mirror = new List<LiveNode>(newLength);
        for (int i = 0; i < oldLength; i++)
        {
            if (used[i])
            {
                mirror.Add(old[start + i]);
            }
        }

        // Place right to left. Nodes on the LIS of old positions never move; every other matched
        // node moves once, directly in front of the already-placed node to its right.
        bool[] stable = ChildMatcher.LongestIncreasingSubsequence(sources);
        int anchor = mirror.Count;
        for (int j = newLength - 1; j >= 0; j--)
        {
            if (sources[j] < 0)
            {
                LiveNode mounted = Mount(next[start + j], parent, slotType);
                collection.Insert(start + anchor, mounted.Instance);
                mirror.Insert(anchor, mounted);
                result[start + j] = mounted;
                continue;
            }

            LiveNode node = old[start + sources[j]];
            int current = mirror.IndexOf(node);
            if (!stable[j])
            {
                int target = current < anchor ? anchor - 1 : anchor;
                if (target != current)
                {
                    collection.Move(start + current, start + target);
                    mirror.RemoveAt(current);
                    mirror.Insert(target, node);
                    Statistics.NodesMoved++;
                    current = target;
                }
            }

            anchor = current;
            result[start + j] = node;
        }

        for (int j = 0; j < newLength; j++)
        {
            if (sources[j] >= 0)
            {
                result[start + j] = PatchAt(collection, start + j, result[start + j], next[start + j], slotType);
            }
        }
    }

    private LiveNode PatchAt(IChildCollection collection, int index, LiveNode live, VNode next, Type slotType)
    {
        LiveNode patched = Patch(live, next, slotType);
        if (!ReferenceEquals(patched, live))
        {
            collection.SetAt(index, patched.Instance);
        }

        return patched;
    }

    private static bool IsSameNode(LiveNode live, VNode next) =>
        !live.IsPlaceholder && ChildMatcher.IsSameIdentity(live.Node, next);
}
