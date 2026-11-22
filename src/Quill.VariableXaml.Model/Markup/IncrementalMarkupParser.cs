using Quill.VariableXaml.Model;

namespace Quill.VariableXaml.Markup;

/// <summary>
/// Re-parses only the smallest element that encloses a text edit, then splices the new subtree
/// into the previous virtual tree. Untouched subtrees keep their object identity, which lets the
/// reconciler skip them by reference.
/// </summary>
/// <remarks>
/// The edit range is the region between the common prefix and the common suffix of the old and
/// new text. Candidate elements must enclose it strictly (the edit cannot touch their first
/// <c>&lt;</c> or last <c>&gt;</c>), and the re-parse must end exactly where the old element ended
/// (shifted by the length delta); otherwise the next enclosing element is tried, and finally a
/// full parse runs (which also produces accurate error positions).
/// </remarks>
public sealed class IncrementalMarkupParser
{
    private readonly XamlMarkupParser _parser;

    public IncrementalMarkupParser(XamlMarkupParser parser) => _parser = parser;

    public ParseOutcome Parse(string text, MarkupDocument? previous, bool allowIncremental = true)
    {
        if (previous is not null && string.Equals(previous.Text, text, StringComparison.Ordinal))
        {
            return new ParseOutcome(previous, ParseKind.Unchanged, 0, null);
        }

        if (previous is not null && allowIncremental && TryParseIncrementally(previous, text, out ParseOutcome? outcome))
        {
            return outcome;
        }

        MarkupDocument document = _parser.Parse(text);
        return new ParseOutcome(document, ParseKind.Full, text.Length, document.Root.Type.LocalName);
    }

    private bool TryParseIncrementally(MarkupDocument previous, string text, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ParseOutcome? outcome)
    {
        outcome = null;
        string oldText = previous.Text;
        int prefix = CommonPrefixLength(oldText, text);
        int suffix = CommonSuffixLength(oldText, text, Math.Min(oldText.Length, text.Length) - prefix);
        int oldChangeEnd = oldText.Length - suffix;
        int delta = text.Length - oldText.Length;

        List<int> chain = FindEnclosingChain(previous.Spans, prefix, oldChangeEnd);

        // chain[0] is the root element; re-parsing it is no cheaper than a full parse.
        for (int depth = chain.Count - 1; depth >= 1; depth--)
        {
            ElementSpan target = previous.Spans[chain[depth]];
            var newSpans = new List<ElementSpan>(target.SubtreeLength + 4);
            VNode node;
            int end;
            try
            {
                node = _parser.ParseElementAt(text, target.Start, target.Scope, newSpans, out end);
            }
            catch (XamlMarkupException)
            {
                continue;
            }

            bool becameIgnorable = node.Type.Namespace != XamlNamespaces.VariableXaml && target.Scope.IsIgnorable(node.Type.Namespace);
            if (end != target.End + delta || becameIgnorable)
            {
                continue;
            }

            MarkupDocument document = Splice(previous, text, chain[depth], node, newSpans, delta);
            outcome = new ParseOutcome(document, ParseKind.Incremental, end - target.Start, node.Type.LocalName);
            return true;
        }

        return false;
    }

    private static List<int> FindEnclosingChain(ElementSpan[] spans, int changeStart, int changeEnd)
    {
        var chain = new List<int>();
        if (spans.Length == 0 || !Encloses(spans[0], changeStart, changeEnd))
        {
            return chain;
        }

        int current = 0;
        chain.Add(current);
        while (true)
        {
            int next = -1;
            int limit = current + spans[current].SubtreeLength;
            for (int child = current + 1; child < limit; child += spans[child].SubtreeLength)
            {
                if (Encloses(spans[child], changeStart, changeEnd))
                {
                    next = child;
                    break;
                }

                if (spans[child].Start >= changeEnd)
                {
                    break;
                }
            }

            if (next < 0)
            {
                return chain;
            }

            chain.Add(next);
            current = next;
        }
    }

    private static bool Encloses(in ElementSpan span, int changeStart, int changeEnd) =>
        span.Start < changeStart && changeEnd < span.End;

    private static MarkupDocument Splice(MarkupDocument previous, string text, int index, VNode node, List<ElementSpan> newSpans, int delta)
    {
        ElementSpan[] old = previous.Spans;
        ElementSpan replaced = old[index];
        int oldCount = replaced.SubtreeLength;
        int shift = newSpans.Count - oldCount;
        var spans = new ElementSpan[old.Length + shift];

        Array.Copy(old, 0, spans, 0, index);

        for (int k = 0; k < newSpans.Count; k++)
        {
            ElementSpan span = newSpans[k];
            if (k == 0)
            {
                span.ParentIndex = replaced.ParentIndex;
                span.Slot = replaced.Slot;
                span.PropertyIndex = replaced.PropertyIndex;
                span.SlotIndex = replaced.SlotIndex;
            }
            else
            {
                span.ParentIndex += index;
            }

            spans[index + k] = span;
        }

        for (int k = index + oldCount; k < old.Length; k++)
        {
            ElementSpan span = old[k];
            span.Start += delta;
            span.End += delta;
            if (span.ParentIndex >= index + oldCount)
            {
                span.ParentIndex += shift;
            }

            spans[k + shift] = span;
        }

        // Path-copy the ancestors so every untouched sibling subtree is shared by reference.
        VNode child = node;
        ElementSpan childSpan = spans[index];
        for (int parentIndex = childSpan.ParentIndex; parentIndex >= 0; parentIndex = childSpan.ParentIndex)
        {
            ElementSpan parent = spans[parentIndex];
            parent.Node = childSpan.Slot == SlotKind.PropertyValue
                ? parent.Node.WithPropertyValue(childSpan.PropertyIndex, childSpan.SlotIndex, child)
                : parent.Node.WithChild(childSpan.SlotIndex, child);
            parent.End += delta;
            parent.SubtreeLength += shift;
            spans[parentIndex] = parent;

            child = parent.Node;
            childSpan = parent;
        }

        return new MarkupDocument(text, child, spans);
    }

    private static int CommonPrefixLength(string a, string b)
    {
        return a.AsSpan().CommonPrefixLength(b.AsSpan());
    }

    private static int CommonSuffixLength(string a, string b, int max)
    {
        int length = 0;
        while (length < max && a[a.Length - 1 - length] == b[b.Length - 1 - length])
        {
            length++;
        }

        return length;
    }
}
