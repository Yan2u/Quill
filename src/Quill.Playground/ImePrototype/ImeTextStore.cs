namespace Quill.Playground.ImePrototype;

/// <summary>
/// A minimal multi-block text model for the Q6 prototype: plain text blocks, a selection inside
/// one block, an undo stack, one input rule and the composition state of 03. It stands in for the
/// editor session; the real one lives in <c>Quill.Editor</c> (M5.4).
/// </summary>
internal sealed class ImeTextStore : ITextInputClient
{
    private readonly List<(int Id, string Text)> _blocks = [];
    private readonly Stack<Snapshot> _undo = new();
    private readonly Stack<Snapshot> _redo = new();
    private readonly List<CompositionSegment> _segments = [];
    private Snapshot? _compositionStart;
    private int _nextId = 1;

    public ImeTextStore(params string[] blocks)
    {
        foreach (string text in blocks.Length == 0 ? [string.Empty] : blocks)
        {
            _blocks.Add((_nextId++, text));
        }
    }

    /// <summary>Raised after every change of text, selection or composition.</summary>
    public event Action? Changed;

    public long Revision { get; private set; }

    public int BlockIndex { get; private set; }

    public int ActiveBlockId => _blocks[BlockIndex].Id;

    public string ActiveText => _blocks[BlockIndex].Text;

    public (int Anchor, int Head) Selection { get; private set; }

    public bool IsComposing { get; private set; }

    /// <summary>The composition range in the active block: the text the service has written since it started.</summary>
    public (int Start, int End)? CompositionRange { get; private set; }

    public IReadOnlyList<CompositionSegment> CompositionSegments => _segments;

    public int BlockCount => _blocks.Count;

    public int UndoCount => _undo.Count;

    /// <summary>Text updates the composition wrote, for the probes: merged into one undo item at the end.</summary>
    public int CompositionSteps { get; private set; }

    public string TextOf(int index) => _blocks[index].Text;

    public int IdOf(int index) => _blocks[index].Id;

    public string Dump() => string.Join("\n", _blocks.Select(static block => block.Text));

    public bool ReplaceText(int start, int end, string text, int selectionStart, int selectionEnd)
    {
        string current = ActiveText;
        start = Math.Clamp(start, 0, current.Length);
        end = Math.Clamp(end, start, current.Length);
        if (!IsComposing && text is "\r" or "\n" or "\r\n")
        {
            // A line break from the text service is a structure command, not text.
            Selection = (start, end);
            SplitBlock();
            return true;
        }

        if (IsComposing)
        {
            _compositionStart ??= Capture();
            CompositionSteps++;
        }
        else
        {
            PushUndo();
        }

        string next = string.Concat(current.AsSpan(0, start), text, current.AsSpan(end));
        var selection = (Math.Clamp(selectionStart, 0, next.Length), Math.Clamp(selectionEnd, 0, next.Length));
        if (IsComposing)
        {
            var (compositionStart, compositionEnd) = CompositionRange ?? (start, start);
            compositionStart = Math.Min(compositionStart, start);
            compositionEnd = Math.Max(compositionEnd, end) - (end - start) + text.Length;
            CompositionRange = (compositionStart, Math.Clamp(compositionEnd, compositionStart, next.Length));
            _segments.Clear();
        }
        else if (text == ">" && start > 0 && next[start - 1] == '-')
        {
            // The input rule: "->" becomes an arrow. Input rules never run inside a composition (03).
            next = string.Concat(next.AsSpan(0, start - 1), "→", next.AsSpan(start + 1));
            selection = (start, start);
        }

        SetActiveText(next);
        Selection = selection;
        RaiseChanged();
        return true;
    }

    public void InsertText(string text)
    {
        var (anchor, head) = Selection;
        int start = Math.Min(anchor, head);
        ReplaceText(start, Math.Max(anchor, head), text, start + text.Length, start + text.Length);
    }

    public void SetSelection(int anchor, int head)
    {
        int length = ActiveText.Length;
        Selection = (Math.Clamp(anchor, 0, length), Math.Clamp(head, 0, length));
        RaiseChanged();
    }

    public void BeginComposition()
    {
        IsComposing = true;
        CompositionRange = null;
        CompositionSteps = 0;
        _compositionStart = null;
        _segments.Clear();
        RaiseChanged();
    }

    public void EndComposition(bool canceled)
    {
        if (_compositionStart is { } before && !before.Matches(this))
        {
            _undo.Push(before);
            _redo.Clear();
        }

        IsComposing = false;
        CompositionRange = null;
        _compositionStart = null;
        _segments.Clear();
        RaiseChanged();
    }

    public void SetCompositionSegment(CompositionSegment segment)
    {
        _segments.RemoveAll(existing => existing.Start < segment.End && segment.Start < existing.End);
        _segments.Add(segment);
        RaiseChanged();
    }

    /// <summary>Runs a semantic key (07: keys go through the key map, text only through CoreText).</summary>
    public bool ExecuteKey(ImeKey key, bool shift)
    {
        if (IsComposing)
        {
            // The text service owns every key during a composition.
            return false;
        }

        var (anchor, head) = Selection;
        string text = ActiveText;
        switch (key)
        {
            case ImeKey.Left:
                MoveHead(head > 0 ? head - StepBack(text, head) : head, shift);
                return true;
            case ImeKey.Right:
                MoveHead(head < text.Length ? head + StepForward(text, head) : head, shift);
                return true;
            case ImeKey.Home:
                MoveHead(0, shift);
                return true;
            case ImeKey.End:
                MoveHead(text.Length, shift);
                return true;
            case ImeKey.Up:
            case ImeKey.Down:
                int target = BlockIndex + (key == ImeKey.Up ? -1 : 1);
                if (target >= 0 && target < _blocks.Count)
                {
                    BlockIndex = target;
                    int offset = Math.Min(head, ActiveText.Length);
                    Selection = (offset, offset);
                    RaiseChanged();
                }

                return true;
            case ImeKey.Backspace:
                if (anchor != head)
                {
                    DeleteRange(Math.Min(anchor, head), Math.Max(anchor, head));
                }
                else if (head > 0)
                {
                    DeleteRange(head - StepBack(text, head), head);
                }
                else if (BlockIndex > 0)
                {
                    PushUndo();
                    string previous = _blocks[BlockIndex - 1].Text;
                    _blocks[BlockIndex - 1] = (_blocks[BlockIndex - 1].Id, previous + text);
                    _blocks.RemoveAt(BlockIndex);
                    BlockIndex--;
                    Selection = (previous.Length, previous.Length);
                    Revision++;
                    RaiseChanged();
                }

                return true;
            case ImeKey.Delete:
                if (anchor != head)
                {
                    DeleteRange(Math.Min(anchor, head), Math.Max(anchor, head));
                }
                else if (head < text.Length)
                {
                    DeleteRange(head, head + StepForward(text, head));
                }

                return true;
            case ImeKey.Enter:
                SplitBlock();
                return true;
            case ImeKey.Tab:
                return ReplaceText(Math.Min(anchor, head), Math.Max(anchor, head), "\t", Math.Min(anchor, head) + 1, Math.Min(anchor, head) + 1);
            case ImeKey.Undo:
                return Restore(_undo, _redo);
            case ImeKey.Redo:
                return Restore(_redo, _undo);
            default:
                return false;
        }
    }

    /// <summary>Places the caret, as a click does.</summary>
    public void PlaceCaret(int blockIndex, int offset)
    {
        BlockIndex = Math.Clamp(blockIndex, 0, _blocks.Count - 1);
        offset = Math.Clamp(offset, 0, ActiveText.Length);
        Selection = (offset, offset);
        RaiseChanged();
    }

    private static int StepBack(string text, int offset) =>
        offset >= 2 && char.IsLowSurrogate(text[offset - 1]) && char.IsHighSurrogate(text[offset - 2]) ? 2 : 1;

    private static int StepForward(string text, int offset) =>
        offset + 1 < text.Length && char.IsHighSurrogate(text[offset]) && char.IsLowSurrogate(text[offset + 1]) ? 2 : 1;

    private void MoveHead(int head, bool extend)
    {
        Selection = (extend ? Selection.Anchor : head, head);
        RaiseChanged();
    }

    private void DeleteRange(int start, int end)
    {
        PushUndo();
        SetActiveText(ActiveText.Remove(start, end - start));
        Selection = (start, start);
        RaiseChanged();
    }

    private void SplitBlock()
    {
        PushUndo();
        var (anchor, head) = Selection;
        int start = Math.Min(anchor, head);
        int end = Math.Max(anchor, head);
        string text = ActiveText;
        _blocks[BlockIndex] = (ActiveBlockId, text[..start]);
        _blocks.Insert(BlockIndex + 1, (_nextId++, text[end..]));
        BlockIndex++;
        Selection = (0, 0);
        Revision++;
        RaiseChanged();
    }

    private void SetActiveText(string text)
    {
        _blocks[BlockIndex] = (ActiveBlockId, text);
        Revision++;
    }

    private void PushUndo()
    {
        _undo.Push(Capture());
        _redo.Clear();
    }

    private bool Restore(Stack<Snapshot> from, Stack<Snapshot> to)
    {
        if (!from.TryPop(out var snapshot))
        {
            return true;
        }

        to.Push(Capture());
        _blocks.Clear();
        _blocks.AddRange(snapshot.Blocks);
        BlockIndex = snapshot.BlockIndex;
        Selection = snapshot.Selection;
        Revision++;
        RaiseChanged();
        return true;
    }

    private Snapshot Capture() => new([.. _blocks], BlockIndex, Selection);

    private void RaiseChanged() => Changed?.Invoke();

    private sealed record Snapshot((int Id, string Text)[] Blocks, int BlockIndex, (int Anchor, int Head) Selection)
    {
        public bool Matches(ImeTextStore store) => Blocks.SequenceEqual(store._blocks) && BlockIndex == store.BlockIndex;
    }
}

/// <summary>The semantic keys of the prototype's key map.</summary>
internal enum ImeKey
{
    Left,
    Right,
    Up,
    Down,
    Home,
    End,
    Backspace,
    Delete,
    Enter,
    Tab,
    Undo,
    Redo,
}
