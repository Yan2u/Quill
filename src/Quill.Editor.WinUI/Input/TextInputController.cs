// Portions of this file are adapted from Folia (src/app-winui/editor/interaction/EditorTextInputController.cpp),
// Copyright (c) 2026 water2004, licensed under the MIT License. See THIRD-PARTY-NOTICES.md.
// Changes: ported to C#; composition events added (07, rules 7-8); the 250 ms de-duplication window
// between the character path and CoreText replaced by an acknowledgement ledger matched by
// revision, block and position (07, rule 5, Q66).

using Quill.Editor.Documents;
using Quill.Editor.Input;
using Windows.Foundation;
using Windows.UI.Text;
using Windows.UI.Text.Core;

namespace Quill.Editor.WinUI.Input;

/// <summary>The rectangles a text service asks for, in physical screen pixels (07, rule 6).</summary>
public readonly record struct TextInputLayoutBounds(Rect Text, Rect Control);

/// <summary>
/// Connects a text service (CoreTextEditContext) to an <see cref="ITextInputClient"/> following the
/// rules of 07: the service sees the active leaf only; the text cache is updated before every
/// notification; text updates run as commands and any difference to the service's prediction is
/// resynchronized after the callback. Two paths deliver text (Q66): CoreText for everything the
/// input method writes (compositions, Chinese punctuation), and <c>CharacterReceived</c> for keys it
/// passes through (Microsoft Pinyin in English mode sends no TextUpdating at all). Enter runs
/// through the key map. Text one path already applied is recorded, and the same text arriving
/// through the other path is matched as an acknowledgement by revision, block and position, never
/// by a time window.
/// </summary>
public sealed class TextInputController
{
    private const int MaximumPendingAcknowledgements = 32;

    private readonly ITextInputClient _client;
    private readonly Func<Action, bool> _enqueue;
    private readonly Func<TextInputLayoutBounds?> _layout;
    private readonly Queue<PendingKey> _pendingKeys = new();
    private ITextServiceContext? _context;

    // What the text service currently believes (07, rule 3): served by TextRequested.
    private BlockId? _knownBlock;
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

    /// <param name="client">The editor side.</param>
    /// <param name="enqueue">Runs an action after the current callback returns (the dispatcher).</param>
    /// <param name="layout">The caret and control rectangles for LayoutRequested.</param>
    public TextInputController(ITextInputClient client, Func<Action, bool> enqueue, Func<TextInputLayoutBounds?> layout)
    {
        _client = client;
        _enqueue = enqueue;
        _layout = layout;
    }

    /// <summary>Receives a line for every service event, for diagnosis.</summary>
    public Action<string>? Trace { get; set; }

    /// <summary>Runs before synchronizing changes back to the text service (e.g., to flush deferred UI layout).</summary>
    public Action? BeforeSynchronize { get; set; }

    public bool IsAttached => _context is not null;

    public bool IsFocused => _focused;

    internal bool IsUpdating => _updating;

    /// <summary>The text the service sees.</summary>
    public string KnownText => _knownText;

    /// <summary>Full resynchronizations so far.</summary>
    public int FullSynchronizations { get; private set; }

    /// <summary>Text updates or characters that acknowledged text the other path already applied.</summary>
    public int Acknowledgements { get; private set; }

    public void Attach(ITextServiceContext context) =>
        Attach(context, previousKnownText: null, alreadyFocused: false);

    public void Attach(ITextServiceContext context, string? previousKnownText, bool alreadyFocused)
    {
        _context = context;
        _focused = alreadyFocused;
        if (previousKnownText is not null)
        {
            // An existing CoreText context still holds the previous text buffer; replace the whole range on sync (07, rule 3).
            _knownBlock = null;
            _knownText = previousKnownText;
            _knownRevision = -1;
            _knownSelection = default;
            _forceFullSynchronization = true;
            QueueSynchronization();
        }
        else
        {
            _knownBlock = _client.ActiveBlock;
            _knownText = _client.ActiveText;
            _knownRevision = _client.Revision;
            _knownSelection = CurrentSelection();
            _forceFullSynchronization = false;
        }
    }

    internal void DisconnectWithoutDetachingContext()
    {
        _context = null;
        _focused = false;
        _synchronizationQueued = false;
        _pendingHighSurrogate = null;
        _pendingKeys.Clear();
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
        _notifying = true;
        try
        {
            Log("NotifyFocusEnter");
            _context.NotifyFocusEnter();
            _context.NotifySelectionChanged(_knownSelection = CurrentSelection());
        }
        catch (Exception exception)
        {
            Log($"  NotifyFocusEnter threw {exception.GetType().Name}: {exception.Message}");
            _forceFullSynchronization = true;
        }
        finally
        {
            _notifying = false;
        }
    }

    public void FocusLeave()
    {
        if (_context is null || !_focused)
        {
            return;
        }

        _focused = false;
        _pendingHighSurrogate = null;
        _notifying = true;
        try
        {
            Log("NotifyFocusLeave");
            _context.NotifyFocusLeave();
        }
        catch (Exception exception)
        {
            Log($"  NotifyFocusLeave threw {exception.GetType().Name}: {exception.Message}");
        }
        finally
        {
            _notifying = false;
        }
    }

    /// <summary>
    /// Called after every change of the client (and after layout, 07 rule 9): synchronizes now, or
    /// after the current callback returns when the change happened inside one (rule 4).
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
        Record(new PendingKey(expectedText, _client.ActiveBlock, Math.Min(_client.Selection.Anchor, _client.Selection.Head), _client.Revision));

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
            Log($"  character {Quote(text)} acknowledges the text update CoreText already applied");
            return true;
        }

        _lastServiceInsert = null;
        var (anchor, head) = _client.Selection;
        Record(new PendingKey(text, _client.ActiveBlock, Math.Min(anchor, head), _client.Revision));
        _client.InsertText(text);
        return true;
    }

    public string OnTextRequested(CoreTextRange range)
    {
        int start = Math.Clamp(range.StartCaretPosition, 0, _knownText.Length);
        int end = Math.Clamp(range.EndCaretPosition, start, _knownText.Length);
        string text = _knownText[start..end];
        Log($"TextRequested {Format(range)} -> {Quote(text)}");
        return text;
    }

    public CoreTextRange OnSelectionRequested()
    {
        var selection = _knownBlock == _client.ActiveBlock ? CurrentSelection() : default;
        Log($"SelectionRequested -> {Format(selection)}");
        return selection;
    }

    public TextInputLayoutBounds? OnLayoutRequested() => _layout();

    public bool OnSelectionUpdating(CoreTextRange selection)
    {
        Log($"SelectionUpdating {Format(selection)}");
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
        QueueSynchronization();
        return true;
    }

    public CoreTextTextUpdatingResult OnTextUpdating(CoreTextRange range, string text, CoreTextRange newSelection, bool canceled)
    {
        Log($"TextUpdating {Format(range)} {Quote(text)} newSelection {Format(newSelection)}{(canceled ? " canceled" : string.Empty)} composing={_client.IsComposing}");
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
            // It now holds the predicted text, which the queued synchronization replaces as a whole.
            Acknowledgements++;
            Log($"  acknowledged {Quote(pending.Text)} recorded at revision {pending.Revision}");
            _knownText = predicted;
            _knownSelection = newSelection;
            _forceFullSynchronization = true;
            QueueSynchronization();
            return CoreTextTextUpdatingResult.Succeeded;
        }

        var previousBlock = _knownBlock;
        bool composing = _client.IsComposing;
        _updating = true;
        bool executed;
        try
        {
            executed = _client.ReplaceText(start, end, text, newSelection.StartCaretPosition, newSelection.EndCaretPosition);
        }
        catch (Exception exception)
        {
            Log($"  command threw {exception.GetType().Name}: {exception.Message}");
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

        // A composition in a virtual paragraph makes a paragraph: a new block, the same text (07, rule 10).
        bool sameBlock = _client.ActiveBlock == previousBlock || (previousBlock is null && composing);
        if (sameBlock && _client.ActiveText == predicted)
        {
            _knownBlock = _client.ActiveBlock;
            _knownRevision = _client.Revision;
        }
        else
        {
            Log($"  the command produced {Quote(_client.ActiveText)} in {_client.ActiveBlock}; resynchronizing after the callback");
            _forceFullSynchronization = true;
        }

        QueueSynchronization();
        return CoreTextTextUpdatingResult.Succeeded;
    }

    /// <summary>
    /// The input method's segment formats. Microsoft Pinyin sends one dotted underline for the whole
    /// composition (Q6), which the projection draws from the composition range.
    /// </summary>
    /// <remarks>TODO(07 rule 7): keep per-segment formats in the state's Composition once an input method needs them.</remarks>
    public CoreTextFormatUpdatingResult OnFormatUpdating(CoreTextRange range, CoreTextFormatUpdatingReason reason, UnderlineType? underline, bool canceled)
    {
        Log($"FormatUpdating {Format(range)} reason={reason} underline={underline?.ToString() ?? "none"}");
        return canceled ? CoreTextFormatUpdatingResult.Failed : CoreTextFormatUpdatingResult.Succeeded;
    }

    public void OnCompositionStarted()
    {
        Log("CompositionStarted");
        _updating = true;
        try
        {
            _client.BeginComposition();
        }
        finally
        {
            _updating = false;
        }

        QueueSynchronization();
    }

    public void OnCompositionCompleted()
    {
        Log("CompositionCompleted");
        _updating = true;
        try
        {
            _client.EndComposition();
        }
        finally
        {
            _updating = false;
        }

        QueueSynchronization();
    }

    public void OnFocusRemoved()
    {
        Log("FocusRemoved");
        _focused = false;
    }

    private static string Quote(string text) => "\"" + text.Replace("\r", "\\r").Replace("\n", "\\n") + "\"";

    private static string Format(CoreTextRange range) => $"[{range.StartCaretPosition},{range.EndCaretPosition})";

    private static bool Same(CoreTextRange left, CoreTextRange right) =>
        left.StartCaretPosition == right.StartCaretPosition && left.EndCaretPosition == right.EndCaretPosition;

    private static bool IsLineBreak(string text) => text is "\r" or "\n" or "\r\n";

    private void Log(string message) => Trace?.Invoke(message);

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
    /// the same block at the recorded offset or just after it.
    /// </summary>
    private PendingKey? TakeAcknowledgement(string text, int start)
    {
        bool isLineBreak = IsLineBreak(text);
        PendingKey? match = null;
        foreach (var key in _pendingKeys)
        {
            bool matches = isLineBreak
                ? IsLineBreak(key.Text)
                : key.Text == text && key.Block == _client.ActiveBlock && (start == key.Offset || start == key.Offset + key.Text.Length);
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

        BeforeSynchronize?.Invoke();
        var block = _client.ActiveBlock;
        string current = _client.ActiveText;
        var selection = CurrentSelection();
        if (!_forceFullSynchronization && block == _knownBlock && _knownRevision == _client.Revision && _knownText == current)
        {
            _notifying = true;
            try
            {
                if (!Same(selection, _knownSelection))
                {
                    _knownSelection = selection;
                    Log($"NotifySelectionChanged {Format(selection)}");
                    _context.NotifySelectionChanged(selection);
                }

                _context.NotifyLayoutChanged();
            }
            catch (Exception exception)
            {
                Log($"  NotifySelectionChanged threw {exception.GetType().Name}: {exception.Message}");
                _forceFullSynchronization = true;
            }
            finally
            {
                _notifying = false;
            }

            return;
        }

        if (_client.IsComposing && block == _knownBlock && _knownText == current)
        {
            // Nothing the service does not know: a full notification would end its composition.
            _knownRevision = _client.Revision;
            _notifying = true;
            try
            {
                _context.NotifyLayoutChanged();
            }
            catch (Exception exception)
            {
                Log($"  NotifyLayoutChanged threw {exception.GetType().Name}: {exception.Message}");
                _forceFullSynchronization = true;
            }
            finally
            {
                _notifying = false;
            }

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
            Log($"NotifyTextChanged [0,{previousLength}) -> {current.Length} selection {Format(selection)} ({block})");
            _context.NotifyTextChanged(new CoreTextRange { StartCaretPosition = 0, EndCaretPosition = previousLength }, current.Length, selection);
            _context.NotifyLayoutChanged();
        }
        catch (Exception exception)
        {
            // The editor stays authoritative; describe the replaced range correctly next time.
            Log($"  NotifyTextChanged threw {exception.GetType().Name}: {exception.Message}");
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

    private readonly record struct PendingKey(string Text, BlockId? Block, int Offset, long Revision);
}
