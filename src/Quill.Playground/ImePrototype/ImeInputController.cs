// Portions of this file are adapted from Folia (src/app-winui/editor/interaction/EditorTextInputController.cpp),
// Copyright (c) 2026 water2004, licensed under the MIT License. See THIRD-PARTY-NOTICES.md.
// Changes: ported to C#; composition events and format updates added (07, rules 7-8); the
// 250 ms de-duplication window between the character path and CoreText replaced by an
// acknowledgement ledger matched by revision, block and position (07, rule 5).

using Windows.Foundation;
using Windows.UI.Text;
using Windows.UI.Text.Core;

namespace Quill.Playground.ImePrototype;

/// <summary>The rectangles a text service asks for, in physical screen pixels (07, rule 6).</summary>
internal readonly record struct ImeLayoutBounds(Rect Text, Rect Control);

/// <summary>
/// Connects a text service (CoreTextEditContext) to an <see cref="ITextInputClient"/> following the
/// rules of 07: the service sees the active block only; the text cache is updated before every
/// notification; text updates run as commands and any difference to the service's prediction is
/// resynchronized after the callback. Two paths deliver text: CoreText for everything the input
/// method writes (compositions, Chinese punctuation), and <c>CharacterReceived</c> for keys it
/// passes through (Microsoft Pinyin in English mode sends no TextUpdating at all, Q6). Enter runs
/// through the key map. Text one path already applied is recorded, and the same text arriving
/// through the other path is matched as an acknowledgement by revision, block and position, never
/// by a time window.
/// </summary>
internal sealed class ImeInputController
{
    private const int MaximumPendingAcknowledgements = 32;

    private readonly ITextInputClient _client;
    private readonly Func<Action, bool> _enqueue;
    private readonly Func<ImeLayoutBounds?> _layout;
    private readonly ImeEventLog _log;
    private readonly Queue<PendingKey> _pendingKeys = new();
    private ITextServiceContext? _context;

    // What the text service currently believes (07, rule 3): served by TextRequested.
    private int _knownBlock;
    private string _knownText = string.Empty;
    private long _knownRevision;
    private CoreTextRange _knownSelection;
    private bool _forceFullSynchronization;

    private bool _focused;
    private bool _updating;
    private bool _notifying;
    private bool _synchronizationQueued;

    // Text CoreText inserted outside a composition, and the client revision right after it: a
    // CharacterReceived with the same text at that revision is the same keystroke.
    private (string Text, long Revision)? _lastServiceInsert;
    private char? _pendingHighSurrogate;

    public ImeInputController(ITextInputClient client, Func<Action, bool> enqueue, Func<ImeLayoutBounds?> layout, ImeEventLog log)
    {
        _client = client;
        _enqueue = enqueue;
        _layout = layout;
        _log = log;
    }

    public bool IsFocused => _focused;

    /// <summary>The text the service sees, for the probes.</summary>
    public string KnownText => _knownText;

    /// <summary>Full resynchronizations so far, for the probes.</summary>
    public int FullSynchronizations { get; private set; }

    /// <summary>Text updates or characters that acknowledged text the other path already applied.</summary>
    public int Acknowledgements { get; private set; }

    public int PendingAcknowledgements => _pendingKeys.Count;

    public void Attach(ITextServiceContext context)
    {
        _context = context;
        _knownBlock = _client.ActiveBlockId;
        _knownText = _client.ActiveText;
        _knownRevision = _client.Revision;
        _knownSelection = CurrentSelection();
        _forceFullSynchronization = false;
    }

    public void Detach()
    {
        FocusLeave();
        _context?.Detach();
        _context = null;
        _pendingKeys.Clear();
    }

    public void FocusEnter()
    {
        if (_context is null || _focused)
        {
            return;
        }

        Synchronize();
        _focused = true;
        _log.Write("NotifyFocusEnter");
        _context.NotifyFocusEnter();
        _context.NotifySelectionChanged(_knownSelection = CurrentSelection());
    }

    public void FocusLeave()
    {
        if (_context is null || !_focused)
        {
            return;
        }

        _focused = false;
        _pendingHighSurrogate = null;
        _log.Write("NotifyFocusLeave");
        _context.NotifyFocusLeave();
    }

    /// <summary>
    /// Called after every change of the client: synchronizes now, or after the current callback
    /// returns when the change happened inside one (07, rule 4).
    /// </summary>
    public void NotifyClientChanged()
    {
        if (_context is null)
        {
            return;
        }

        if (_updating || _notifying || _synchronizationQueued)
        {
            QueueSynchronization();
            return;
        }

        Synchronize();
    }

    /// <summary>
    /// Records a semantic key (Enter) that the key map already executed; the line break the
    /// service reports for it afterwards is an acknowledgement, not an edit (07, rule 5).
    /// </summary>
    public void RecordSemanticKey(string expectedText) =>
        Record(new PendingKey(expectedText, _client.ActiveBlockId, Math.Min(_client.Selection.Anchor, _client.Selection.Head), _client.Revision));

    /// <summary>
    /// A character from <c>CharacterReceived</c>: the keys the input method passes through. Returns
    /// whether it was consumed. Control characters belong to the key map; nothing is taken during a
    /// composition.
    /// </summary>
    public bool OnCharacterReceived(char character)
    {
        if (_client.IsComposing || char.IsControl(character))
        {
            return false;
        }

        if (char.IsHighSurrogate(character))
        {
            _pendingHighSurrogate = character;
            return true;
        }

        string text = char.IsLowSurrogate(character) && _pendingHighSurrogate is { } high ? $"{high}{character}" : character.ToString();
        _pendingHighSurrogate = null;
        if (_lastServiceInsert is { } last && last.Text == text && last.Revision == _client.Revision)
        {
            _lastServiceInsert = null;
            Acknowledgements++;
            _log.Write($"  character {ImeEventLog.Quote(text)} acknowledges the text update CoreText already applied");
            return true;
        }

        _lastServiceInsert = null;
        var (anchor, head) = _client.Selection;
        Record(new PendingKey(text, _client.ActiveBlockId, Math.Min(anchor, head), _client.Revision));
        _log.Write($"  inserting character {ImeEventLog.Quote(text)}");
        _client.InsertText(text);
        return true;
    }

    public string OnTextRequested(CoreTextRange range)
    {
        int start = Math.Clamp(range.StartCaretPosition, 0, _knownText.Length);
        int end = Math.Clamp(range.EndCaretPosition, start, _knownText.Length);
        string text = _knownText[start..end];
        _log.Write($"TextRequested [{range.StartCaretPosition},{range.EndCaretPosition}) -> {ImeEventLog.Quote(text)}");
        return text;
    }

    public CoreTextRange OnSelectionRequested()
    {
        var selection = _knownBlock == _client.ActiveBlockId ? CurrentSelection() : default;
        _log.Write($"SelectionRequested -> {Format(selection)}");
        return selection;
    }

    public ImeLayoutBounds? OnLayoutRequested()
    {
        var bounds = _layout();
        _log.Write($"LayoutRequested -> text {Format(bounds?.Text)} control {Format(bounds?.Control)}");
        return bounds;
    }

    public bool OnSelectionUpdating(CoreTextRange selection)
    {
        _log.Write($"SelectionUpdating {Format(selection)}");
        int length = _knownText.Length;
        _updating = true;
        try
        {
            _client.SetSelection(Math.Clamp(selection.StartCaretPosition, 0, length), Math.Clamp(selection.EndCaretPosition, 0, length));
        }
        finally
        {
            _updating = false;
        }

        _knownSelection = selection;
        return true;
    }

    public CoreTextTextUpdatingResult OnTextUpdating(CoreTextRange range, string text, CoreTextRange newSelection, bool canceled)
    {
        _log.Write($"TextUpdating {Format(range)} {ImeEventLog.Quote(text)} newSelection {Format(newSelection)}{(canceled ? " canceled" : string.Empty)} composing={_client.IsComposing}");
        if (canceled || _context is null)
        {
            return CoreTextTextUpdatingResult.Failed;
        }

        int start = Math.Clamp(range.StartCaretPosition, 0, _knownText.Length);
        int end = Math.Clamp(range.EndCaretPosition, start, _knownText.Length);
        string predicted = string.Concat(_knownText.AsSpan(0, start), text, _knownText.AsSpan(end));

        if (!_client.IsComposing && TakeAcknowledgement(text, range.StartCaretPosition) is { } pending)
        {
            // The key map or the character path already applied this; the service is catching up.
            Acknowledgements++;
            _log.Write($"  acknowledged {ImeEventLog.Quote(pending.Text)} recorded at revision {pending.Revision}");

            // The service now holds the predicted text; the queued synchronization replaces it as a whole.
            _knownText = predicted;
            _knownSelection = newSelection;
            _forceFullSynchronization = true;
            QueueSynchronization();
            return CoreTextTextUpdatingResult.Succeeded;
        }

        int previousBlock = _knownBlock;
        bool composing = _client.IsComposing;

        _updating = true;
        bool executed;
        try
        {
            executed = _client.ReplaceText(start, end, text, newSelection.StartCaretPosition, newSelection.EndCaretPosition);
        }
        catch (Exception exception)
        {
            _log.Write($"  command threw {exception.GetType().Name}: {exception.Message}");
            executed = false;
        }
        finally
        {
            _updating = false;
        }

        if (!executed)
        {
            QueueSynchronization();
            return CoreTextTextUpdatingResult.Failed;
        }

        _lastServiceInsert = !composing && text.Length > 0 ? (text, _client.Revision) : null;

        // Once this callback succeeds the service holds the predicted text, whatever the command did.
        _knownText = predicted;
        _knownSelection = newSelection;
        if (_client.ActiveBlockId == previousBlock && _client.ActiveText == predicted)
        {
            _knownRevision = _client.Revision;
        }
        else
        {
            _log.Write($"  the command produced {ImeEventLog.Quote(_client.ActiveText)} in block {_client.ActiveBlockId}; resynchronizing after the callback");
            _forceFullSynchronization = true;
        }

        QueueSynchronization();
        return CoreTextTextUpdatingResult.Succeeded;
    }

    public CoreTextFormatUpdatingResult OnFormatUpdating(CoreTextRange range, CoreTextFormatUpdatingReason reason, UnderlineType? underline, bool canceled)
    {
        _log.Write($"FormatUpdating {Format(range)} reason={reason} underline={underline?.ToString() ?? "none"}{(canceled ? " canceled" : string.Empty)}");
        if (canceled)
        {
            return CoreTextFormatUpdatingResult.Failed;
        }

        var kind = reason switch
        {
            CoreTextFormatUpdatingReason.CompositionUnconverted => CompositionSegmentKind.Unconverted,
            CoreTextFormatUpdatingReason.CompositionConverted => CompositionSegmentKind.Converted,
            CoreTextFormatUpdatingReason.CompositionTargetUnconverted => CompositionSegmentKind.TargetUnconverted,
            CoreTextFormatUpdatingReason.CompositionTargetConverted => CompositionSegmentKind.TargetConverted,
            _ => CompositionSegmentKind.None,
        };
        bool thick = underline is UnderlineType.Thick or UnderlineType.ThickDash or UnderlineType.ThickDotted;
        _updating = true;
        try
        {
            _client.SetCompositionSegment(new CompositionSegment(range.StartCaretPosition, range.EndCaretPosition, kind, thick));
        }
        finally
        {
            _updating = false;
        }

        return CoreTextFormatUpdatingResult.Succeeded;
    }

    public void OnCompositionStarted()
    {
        _log.Write("CompositionStarted");
        _updating = true;
        try
        {
            _client.BeginComposition();
        }
        finally
        {
            _updating = false;
        }
    }

    public void OnCompositionCompleted(bool canceled)
    {
        _log.Write($"CompositionCompleted{(canceled ? " canceled" : string.Empty)}");
        _updating = true;
        try
        {
            _client.EndComposition(canceled);
        }
        finally
        {
            _updating = false;
        }

        QueueSynchronization();
    }

    public void OnFocusRemoved()
    {
        _log.Write("FocusRemoved (by the text service)");
        _focused = false;
    }

    private void Record(PendingKey key)
    {
        _pendingKeys.Enqueue(key);
        while (_pendingKeys.Count > MaximumPendingAcknowledgements)
        {
            _pendingKeys.Dequeue();
        }
    }

    /// <summary>
    /// Finds and removes the recorded text a CoreText update repeats: a line break for a pending
    /// Enter (the block has changed since, so only the order counts), or the same characters in
    /// the same block at the recorded offset or just after it (before or after the service saw
    /// the resynchronization).
    /// </summary>
    private PendingKey? TakeAcknowledgement(string text, int start)
    {
        bool isLineBreak = IsLineBreak(text);
        PendingKey? match = null;
        foreach (var key in _pendingKeys)
        {
            bool matches = isLineBreak
                ? IsLineBreak(key.Text)
                : key.Text == text && key.Block == _client.ActiveBlockId && (start == key.Offset || start == key.Offset + key.Text.Length);
            if (matches)
            {
                match = key;
                break;
            }
        }

        if (match is { } found)
        {
            var rest = _pendingKeys.Where(key => key != found).ToArray();
            _pendingKeys.Clear();
            foreach (var key in rest)
            {
                _pendingKeys.Enqueue(key);
            }
        }

        return match;
    }

    private static bool IsLineBreak(string text) => text is "\r" or "\n" or "\r\n";

    private void QueueSynchronization()
    {
        if (_synchronizationQueued || _context is null)
        {
            return;
        }

        _synchronizationQueued = true;
        if (!_enqueue(() =>
            {
                _synchronizationQueued = false;
                Synchronize();
            }))
        {
            _synchronizationQueued = false;
        }
    }

    private void Synchronize()
    {
        if (_context is null)
        {
            return;
        }

        if (_updating || _notifying)
        {
            QueueSynchronization();
            return;
        }

        int block = _client.ActiveBlockId;
        string current = _client.ActiveText;
        var selection = CurrentSelection();
        if (!_forceFullSynchronization && block == _knownBlock && _knownRevision == _client.Revision && _knownText == current)
        {
            if (!Same(selection, _knownSelection))
            {
                _knownSelection = selection;
                _log.Write($"NotifySelectionChanged {Format(selection)}");
                _context.NotifySelectionChanged(selection);
            }

            _context.NotifyLayoutChanged();
            return;
        }

        // The service may call back synchronously from NotifyTextChanged: the cache must already
        // hold the new text, with only the replaced range taken from the old length (07, rule 3).
        int previousLength = _knownText.Length;
        _notifying = true;
        try
        {
            _knownBlock = block;
            _knownText = current;
            _knownRevision = _client.Revision;
            _knownSelection = selection;
            _forceFullSynchronization = false;
            FullSynchronizations++;
            _log.Write($"NotifyTextChanged [0,{previousLength}) -> {current.Length} selection {Format(selection)} (block {block})");
            _context.NotifyTextChanged(new CoreTextRange { StartCaretPosition = 0, EndCaretPosition = previousLength }, current.Length, selection);
            _context.NotifyLayoutChanged();
        }
        catch (Exception exception)
        {
            // The editor stays authoritative; describe the replaced range correctly next time.
            _log.Write($"  NotifyTextChanged threw {exception.GetType().Name}: {exception.Message}");
            _forceFullSynchronization = true;
        }
        finally
        {
            _notifying = false;
        }
    }

    private CoreTextRange CurrentSelection()
    {
        var (anchor, head) = _client.Selection;
        return new CoreTextRange { StartCaretPosition = Math.Min(anchor, head), EndCaretPosition = Math.Max(anchor, head) };
    }

    private static bool Same(CoreTextRange left, CoreTextRange right) =>
        left.StartCaretPosition == right.StartCaretPosition && left.EndCaretPosition == right.EndCaretPosition;

    private static string Format(CoreTextRange range) => $"[{range.StartCaretPosition},{range.EndCaretPosition})";

    private static string Format(Rect? rect) =>
        rect is { } r ? $"({r.X:F0},{r.Y:F0} {r.Width:F0}x{r.Height:F0})" : "none";

    private readonly record struct PendingKey(string Text, int Block, int Offset, long Revision);
}
