using Quill.Editor.Documents;

namespace Quill.Editor.State;

/// <summary>
/// Orders positions in document order. A boundary lies before everything in the child it precedes
/// and after everything in the child before it.
/// </summary>
public static class PositionOrder
{
    public static int Compare(Document document, Position left, Position right)
    {
        var a = Key(document, left);
        var b = Key(document, right);
        int length = Math.Min(a.Count, b.Count);
        for (int i = 0; i < length; i++)
        {
            int compared = a[i].CompareTo(b[i]);
            if (compared != 0)
            {
                return compared;
            }
        }

        return a.Count.CompareTo(b.Count);
    }

    public static (Position Start, Position End) Ordered(Document document, TextSelection selection) =>
        Compare(document, selection.Anchor, selection.Head) <= 0 ? (selection.Anchor, selection.Head) : (selection.Head, selection.Anchor);

    /// <summary>The child indices from the root to the position, then the offset or boundary index.</summary>
    private static List<int> Key(Document document, Position position)
    {
        BlockId owner = position is TextPosition text ? text.Block : ((BoundaryPosition)position).Parent;
        var location = DocumentTree.Locate(document, owner);
        var key = new List<int>(location.Ancestors.Length + 1);
        foreach (var (_, index) in location.Ancestors)
        {
            key.Add(index);
        }

        key.Add(position is TextPosition t ? t.Offset : ((BoundaryPosition)position).Index);
        return key;
    }
}
