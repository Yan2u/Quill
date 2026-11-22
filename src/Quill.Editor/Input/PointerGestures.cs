using Quill.Editor.Commands;
using Quill.Editor.Services;
using Quill.Editor.State;

namespace Quill.Editor.Input;

/// <summary>A pointer event in view coordinates (DIPs relative to the editor surface, not the document).</summary>
public readonly record struct PointerInput(double X, double Y, KeyModifiers Modifiers);

/// <summary>
/// Turns pointer presses, moves and releases into commands (07): click places the caret, Shift+click
/// extends, double-click selects a word, triple-click the block, a drag extends from the press. The
/// activation is frozen from press to release (03), so nothing reflows under the pointer. The drag
/// threshold is measured from where the pointer went down in view coordinates: a click that expands
/// markers and reflows the text is not mistaken for a drag (Folia). Document positions come from the
/// caller's hit test at the time of each event.
/// </summary>
public sealed class PointerGestures
{
    private readonly IClock _clock;
    private PointerInput _press;
    private DateTimeOffset _pressTime = DateTimeOffset.MinValue;
    private Position? _anchor;
    private bool _down;

    public PointerGestures(IClock clock)
    {
        _clock = clock;
    }

    /// <summary>How far the pointer moves before a press becomes a drag (07: about 4 DIP).</summary>
    public double DragThreshold { get; init; } = 4;

    /// <summary>The longest interval between presses of a double or triple click.</summary>
    public TimeSpan MultiClickTime { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>The farthest the pointer may move between presses of a double or triple click.</summary>
    public double MultiClickDistance { get; init; } = 4;

    public bool IsDown => _down;

    public bool IsDragging { get; private set; }

    /// <summary>1, 2 or 3 for the current press.</summary>
    public int ClickCount { get; private set; }

    public IReadOnlyList<Command> Pressed(PointerInput input, Position hit, EditorState state)
    {
        var now = _clock.Now;
        bool repeat = _down is false && now - _pressTime <= MultiClickTime && Distance(input, _press) <= MultiClickDistance;
        ClickCount = repeat ? ClickCount % 3 + 1 : 1;
        _press = input;
        _pressTime = now;
        _down = true;
        IsDragging = false;

        // Freeze first: the selection below changes while the activation stays where it was (03).
        var commands = new List<Command> { new SetInteraction(state.Interaction with { IsPointerDown = true }) };
        switch (ClickCount)
        {
            case 2:
                commands.Add(new SelectWord(hit));
                _anchor = hit;
                break;
            case 3:
                commands.Add(new SelectBlock(hit));
                _anchor = hit;
                break;
            default:
                bool extend = input.Modifiers.HasFlag(KeyModifiers.Shift);
                _anchor = extend ? state.Selection.Anchor : hit;
                commands.Add(new SetSelection(new TextSelection(_anchor, hit)));
                break;
        }

        return commands;
    }

    /// <summary>A move: nothing until the pointer leaves the threshold, then the selection follows it.</summary>
    public IReadOnlyList<Command> Moved(PointerInput input, Func<Position> hit)
    {
        if (!_down || _anchor is null)
        {
            return [];
        }

        if (!IsDragging && Distance(input, _press) <= DragThreshold)
        {
            return [];
        }

        IsDragging = true;
        return [new SetSelection(new TextSelection(_anchor, hit()))];
    }

    /// <summary>
    /// The release: unfreezes the activation. A Ctrl+click without a drag navigates when the target
    /// function finds something at the press position; the caller handles targets it opens itself.
    /// </summary>
    public IReadOnlyList<Command> Released(PointerInput input, EditorState state, out NavigationTarget? target)
    {
        target = null;
        if (!_down)
        {
            return [];
        }

        _down = false;
        var commands = new List<Command> { new SetInteraction(state.Interaction with { IsPointerDown = false, FrozenActivationHead = null }) };
        if (!IsDragging && ClickCount == 1 && input.Modifiers.HasFlag(KeyModifiers.Ctrl) && _anchor is not null)
        {
            target = NavigationTargets.At(state, _anchor);
            switch (target)
            {
                case FootnoteTarget footnote:
                    commands.Add(new GoToFootnoteDefinition(footnote.Label));
                    break;
                case LinkTarget { IsAnchor: true } link:
                    commands.Add(new GoToAnchor(link.Destination));
                    break;
            }
        }

        IsDragging = false;
        return commands;
    }

    /// <summary>Forgets a press whose release never comes (capture lost).</summary>
    public IReadOnlyList<Command> Cancel(EditorState state)
    {
        if (!_down)
        {
            return [];
        }

        _down = false;
        IsDragging = false;
        return [new SetInteraction(state.Interaction with { IsPointerDown = false, FrozenActivationHead = null })];
    }

    private static double Distance(PointerInput a, PointerInput b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
}
