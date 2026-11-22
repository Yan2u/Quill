using Quill.VariableXaml.Model;

namespace Quill.VariableXaml.Reconciliation;

/// <summary>
/// Matches the middle (non-prefix, non-suffix) part of an old and a new child list.
/// Shared by the live-element reconciler and the virtualized item list.
/// </summary>
internal static class ChildMatcher
{
    /// <summary>
    /// Returns, for each new child, the index of the old child it reuses, or -1 to mount it fresh.
    /// </summary>
    /// <param name="oldNodes">Old children (virtual nodes currently applied).</param>
    /// <param name="isReusable">Whether an old child may be reused at all (e.g. not a placeholder).</param>
    /// <param name="newNodes">New children.</param>
    /// <remarks>
    /// Pass 1 matches keyed children by key (same type required) and unkeyed children by content
    /// hash, so moved or untouched siblings keep their elements. Pass 2 pairs leftover unkeyed
    /// children (or any leftover children when <paramref name="ignoreKey"/> is true, during pooled
    /// subtree reuse) of the same type in document order, so elements are patched in place
    /// instead of being recreated.
    /// </remarks>
    public static int[] Match(IReadOnlyList<VNode> oldNodes, Func<int, bool> isReusable, IReadOnlyList<VNode> newNodes, bool ignoreKey = false)
    {
        int oldLength = oldNodes.Count;
        int newLength = newNodes.Count;
        int[] sources = new int[newLength];
        Array.Fill(sources, -1);
        bool[] used = new bool[oldLength];

        Dictionary<string, int>? byKey = null;
        Dictionary<ulong, Queue<int>>? byHash = null;
        for (int i = 0; i < oldLength; i++)
        {
            if (!isReusable(i))
            {
                continue;
            }

            VNode node = oldNodes[i];
            if (node.Key is { } key)
            {
                (byKey ??= []).TryAdd(key, i);
            }
            else
            {
                Enqueue(byHash ??= [], node.Hash, i);
            }
        }

        for (int j = 0; j < newLength; j++)
        {
            VNode node = newNodes[j];
            if (node.Key is { } key)
            {
                if (byKey is not null && byKey.TryGetValue(key, out int i) && !used[i] && IsSameShape(oldNodes[i], node))
                {
                    sources[j] = i;
                    used[i] = true;
                }
            }
            else if (byHash is not null && byHash.TryGetValue(node.Hash, out Queue<int>? queue) && queue.Count > 0)
            {
                int i = queue.Dequeue();
                sources[j] = i;
                used[i] = true;
            }
        }

        Dictionary<(VNodeKind, XamlName), Queue<int>>? byType = null;
        for (int i = 0; i < oldLength; i++)
        {
            if (!used[i] && (ignoreKey || oldNodes[i].Key is null) && isReusable(i))
            {
                Enqueue(byType ??= [], (oldNodes[i].Kind, oldNodes[i].Type), i);
            }
        }

        if (byType is not null)
        {
            for (int j = 0; j < newLength; j++)
            {
                VNode node = newNodes[j];
                if (sources[j] < 0 && (ignoreKey || node.Key is null) && byType.TryGetValue((node.Kind, node.Type), out Queue<int>? queue) && queue.Count > 0)
                {
                    int i = queue.Dequeue();
                    sources[j] = i;
                    used[i] = true;
                }
            }
        }

        return sources;
    }

    /// <summary>Identity used for common prefixes and suffixes: the same key and type, or identical content.</summary>
    public static bool IsSameIdentity(VNode current, VNode next) =>
        ReferenceEquals(current, next)
        || current.Hash == next.Hash
        || (next.Key is not null && current.Key == next.Key && IsSameShape(current, next));

    /// <summary>Marks the positions whose sources (ignoring -1) form a longest increasing subsequence.</summary>
    public static bool[] LongestIncreasingSubsequence(int[] sources)
    {
        int count = sources.Length;
        var stable = new bool[count];
        var tails = new int[count];
        var previous = new int[count];
        int length = 0;
        for (int j = 0; j < count; j++)
        {
            int value = sources[j];
            if (value < 0)
            {
                continue;
            }

            int low = 0;
            int high = length;
            while (low < high)
            {
                int middle = (low + high) >> 1;
                if (sources[tails[middle]] < value)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            previous[j] = low > 0 ? tails[low - 1] : -1;
            tails[low] = j;
            if (low == length)
            {
                length++;
            }
        }

        for (int j = length > 0 ? tails[length - 1] : -1; j >= 0; j = previous[j])
        {
            stable[j] = true;
        }

        return stable;
    }

    private static bool IsSameShape(VNode a, VNode b) => a.Kind == b.Kind && a.Type == b.Type;

    private static void Enqueue<TKey>(Dictionary<TKey, Queue<int>> map, TKey key, int index)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out Queue<int>? queue))
        {
            map[key] = queue = new Queue<int>();
        }

        queue.Enqueue(index);
    }
}
