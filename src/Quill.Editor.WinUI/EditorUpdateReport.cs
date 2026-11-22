namespace Quill.Editor.WinUI;

/// <summary>
/// The phases of one update of the <see cref="MarkdownBox"/> (09, rule 7): the command with its
/// behaviors, the projection, the reconcile into the element tree, layout, bringing the caret into
/// view and notifying the input method. Drawing happens later, in the frame.
/// </summary>
public sealed record EditorUpdateReport
{
    public required string Command { get; init; }

    public TimeSpan CommandTime { get; init; }

    public TimeSpan ProjectionTime { get; init; }

    public TimeSpan ReconcileTime { get; init; }

    public TimeSpan LayoutTime { get; init; }

    public TimeSpan ScrollTime { get; init; }

    public TimeSpan InputTime { get; init; }

    public TimeSpan Total => CommandTime + ProjectionTime + ReconcileTime + LayoutTime + ScrollTime + InputTime;

    public override string ToString() =>
        $"{Command}: command {Ms(CommandTime)}, projection {Ms(ProjectionTime)}, reconcile {Ms(ReconcileTime)}, layout {Ms(LayoutTime)}, scroll {Ms(ScrollTime)}, input {Ms(InputTime)} ms";

    private static string Ms(TimeSpan time) => time.TotalMilliseconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
}
