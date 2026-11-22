using Quill.Editor.Documents;
using Quill.Editor.State;

namespace Quill.Editor.Services;

/// <summary>
/// Time for undo merging and timeouts (01-architecture.md). Core code never reads the system clock
/// directly, so tests can advance time by hand.
/// </summary>
public interface IClock
{
    DateTimeOffset Now { get; }
}

public sealed class SystemClock : IClock
{
    public static SystemClock Instance { get; } = new();

    public DateTimeOffset Now => DateTimeOffset.UtcNow;
}

/// <summary>A rectangle in layout units (device-independent pixels in the view).</summary>
public readonly record struct LayoutRect(double X, double Y, double Width, double Height)
{
    public double Bottom => Y + Height;
}

/// <summary>A point in layout units.</summary>
public readonly record struct LayoutPoint(double X, double Y);

/// <summary>The visual line holding a position: its first and last positions and its bounds.</summary>
public readonly record struct VisualLine(Position Start, Position End, LayoutRect Bounds);

/// <summary>
/// Layout queries the editing core needs (08-testing-and-debugging.md): caret geometry, hit testing,
/// visual lines and the viewport. Must answer for blocks outside the viewport too: their own geometry
/// is laid out outside the element tree, positions in the document after scrolling to them (Q64).
/// Tests use a fake monospaced layout.
/// </summary>
public interface ILayoutService
{
    LayoutRect GetCaretRect(Position position);

    Position HitTest(LayoutPoint point);

    VisualLine GetVisualLine(Position position);

    /// <summary>
    /// The position at <paramref name="x"/> on the visual line below (<paramref name="down"/>) or above
    /// the line holding <paramref name="position"/>, skipping the space between blocks and blocks
    /// without lines (TOC, rules); null on the first or last line. At a block boundary, the lines
    /// adjacent to the boundary. Vertical caret moves use it instead of hit testing just past the
    /// caret, which lands between blocks in a real layout.
    /// </summary>
    Position? GetAdjacentLine(Position position, double x, bool down);

    LayoutRect GetViewport();

    /// <summary>Scrolls the block into view and completes layout, so document coordinates near it are exact.</summary>
    void ScrollIntoView(BlockId block);
}
