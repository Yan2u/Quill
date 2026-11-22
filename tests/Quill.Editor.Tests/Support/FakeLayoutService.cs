using System.Globalization;
using Quill.Editor.Documents;
using Quill.Editor.Services;
using Quill.Editor.State;

namespace Quill.Editor.Tests.Support;

/// <summary>
/// The fake layout of 08-testing-and-debugging.md: every grapheme cluster is 1 unit wide (CJK 2),
/// lines wrap at <see cref="Columns"/>, every visual line is 1 unit high, and each leaf starts on a
/// new line with no space between blocks. Fully deterministic; recomputed on every query.
/// </summary>
public sealed class FakeLayoutService : ILayoutService
{
    private readonly Func<Document> _document;

    public FakeLayoutService(Func<Document> document, int columns = 80)
    {
        _document = document;
        Columns = columns;
    }

    public int Columns { get; set; }

    public int ViewportHeight { get; set; } = 20;

    private readonly record struct Line(BlockId Leaf, int Start, int End, int Y);

    public LayoutRect GetCaretRect(Position position)
    {
        var lines = Lines();
        if (position is not TextPosition text)
        {
            return new LayoutRect(0, lines.Count, 0, 1);
        }

        var line = LineOf(lines, text);
        var leaf = DocumentTree.Find<LeafBlock>(_document(), text.Block);
        return new LayoutRect(Width(leaf.Text, line.Start, text.Offset), line.Y, 0, 1);
    }

    public Position HitTest(LayoutPoint point)
    {
        var lines = Lines();
        var line = lines[Math.Clamp((int)Math.Floor(point.Y), 0, lines.Count - 1)];
        string text = DocumentTree.Find<LeafBlock>(_document(), line.Leaf).Text;
        double x = 0;
        int offset = line.Start;
        while (offset < line.End)
        {
            int next = offset + StringInfo.GetNextTextElementLength(text, offset);
            double width = Width(text, offset, next);
            if (point.X < x + (width / 2))
            {
                break;
            }

            x += width;
            offset = next;
        }

        // At a wrap point the position belongs to the end of this line.
        var affinity = offset == line.End && line.End < text.Length && text[line.End] != '\n' ? Affinity.Backward : Affinity.Forward;
        return new TextPosition(line.Leaf, offset, affinity);
    }

    public VisualLine GetVisualLine(Position position)
    {
        if (position is not TextPosition text)
        {
            return new VisualLine(position, position, GetCaretRect(position));
        }

        var line = LineOf(Lines(), text);
        var start = new TextPosition(line.Leaf, line.Start);
        var end = new TextPosition(line.Leaf, line.End, Affinity.Backward);
        return new VisualLine(start, end, new LayoutRect(0, line.Y, Columns, 1));
    }

    public LayoutRect GetViewport() => new(0, 0, Columns, ViewportHeight);

    public Position? GetAdjacentLine(Position position, double x, bool down)
    {
        var lines = Lines();
        int target;
        if (position is TextPosition text)
        {
            target = LineOf(lines, text).Y + (down ? 1 : -1);
        }
        else
        {
            // A boundary lies between the last line before it and the first line after it.
            var document = _document();
            int after = lines.FindIndex(line => PositionOrder.Compare(document, position, new TextPosition(line.Leaf, line.Start)) <= 0);
            target = after < 0 ? (down ? lines.Count : lines.Count - 1) : (down ? after : after - 1);
        }

        return target < 0 || target >= lines.Count ? null : HitTest(new LayoutPoint(x, target + 0.5));
    }

    public void ScrollIntoView(BlockId block)
    {
    }

    private static Line LineOf(List<Line> lines, TextPosition position)
    {
        Line? found = null;
        foreach (var line in lines.Where(line => line.Leaf == position.Block))
        {
            if (position.Offset >= line.Start && position.Offset <= line.End)
            {
                found = line;
                if (position.Affinity == Affinity.Backward || position.Offset < line.End)
                {
                    break;
                }
            }
        }

        return found ?? throw new ArgumentException($"No line holds {position}.");
    }

    private List<Line> Lines()
    {
        var lines = new List<Line>();
        foreach (var leaf in Leaves(_document()))
        {
            string text = leaf.Text;
            int lineStart = 0;
            while (true)
            {
                int lineEnd = text.IndexOf('\n', lineStart);
                if (lineEnd < 0)
                {
                    lineEnd = text.Length;
                }

                // Wrap the source line at the column limit.
                int start = lineStart;
                double width = 0;
                int offset = lineStart;
                while (offset < lineEnd)
                {
                    int next = offset + StringInfo.GetNextTextElementLength(text, offset);
                    double w = Width(text, offset, next);
                    if (width + w > Columns && offset > start)
                    {
                        lines.Add(new Line(leaf.Id, start, offset, lines.Count));
                        start = offset;
                        width = 0;
                    }

                    width += w;
                    offset = next;
                }

                lines.Add(new Line(leaf.Id, start, lineEnd, lines.Count));
                if (lineEnd >= text.Length)
                {
                    break;
                }

                lineStart = lineEnd + 1;
            }
        }

        return lines;
    }

    private static IEnumerable<LeafBlock> Leaves(Block block)
    {
        if (block is LeafBlock leaf)
        {
            yield return leaf;
        }
        else if (block is ContainerBlock container)
        {
            foreach (var inner in container.Children.SelectMany(Leaves))
            {
                yield return inner;
            }
        }
    }

    private static double Width(string text, int from, int to)
    {
        double width = 0;
        for (int i = from; i < to; i += StringInfo.GetNextTextElementLength(text, i))
        {
            char c = text[i];
            width += c == '​' ? 0 : c is >= '⺀' and <= '鿿' or >= '가' and <= '힯' or >= '＀' and <= '￯' ? 2 : 1;
        }

        return width;
    }
}
