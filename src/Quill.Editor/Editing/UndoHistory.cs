using System.Collections.Immutable;
using Quill.Editor.Documents;
using Quill.Editor.State;
using Quill.Editor.Transactions;

namespace Quill.Editor.Editing;

/// <summary>
/// One undo item: one or several merged transactions.
/// </summary>
public sealed record HistoryEntry
{
    public required ImmutableArray<Step> Steps { get; init; }

    /// <summary>Steps that undo <see cref="Steps"/>, in the order to apply them.</summary>
    public required ImmutableArray<Step> InverseSteps { get; init; }

    public required TextSelection SelectionBefore { get; init; }

    public required TextSelection SelectionAfter { get; init; }

    /// <summary>The document revision right after the entry; undo requires the document to still be at it.</summary>
    public required long Revision { get; init; }

    public required TransactionKind Kind { get; init; }

    public BlockId? EditedBlock { get; init; }

    public required DateTimeOffset Time { get; init; }

    /// <summary>
    /// A change made in the source mode (06, 11-source-mode.md): the entry has no steps; undoing or
    /// redoing it rebuilds the document from the source text with the change reverted or applied.
    /// </summary>
    public SourceEdit? Source { get; init; }

    /// <summary>Identifies the document content after this entry; a merge into the entry changes it.</summary>
    internal long Sequence { get; init; }

    internal static HistoryEntry From(Transaction transaction) => new()
    {
        Steps = transaction.Steps,
        InverseSteps = transaction.InverseSteps,
        SelectionBefore = transaction.SelectionBefore,
        SelectionAfter = transaction.SelectionAfter,
        Revision = transaction.Result.Revision,
        Kind = transaction.Kind,
        EditedBlock = transaction.EditedBlock,
        Time = transaction.Time,
    };
}

/// <summary>
/// Undo and redo stacks with the merge rules of 04-commands-and-behaviors.md: consecutive typing (or
/// deleting) in one block, where each edit starts where the last one ended, merges into one item until
/// the command kind changes, the caret jumps or the idle time exceeds <see cref="MergeWindow"/>.
/// Input rule conversions and commits are always items of their own.
/// </summary>
public sealed class UndoHistory
{
    private readonly List<HistoryEntry> _undo = [];
    private readonly List<HistoryEntry> _redo = [];
    private bool _mergeBroken = true;
    private long _sequence;
    private long _savedAt;

    /// <summary>The longest pause between two edits that still merge〔默认〕.</summary>
    public TimeSpan MergeWindow { get; init; } = TimeSpan.FromSeconds(1);

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public IReadOnlyList<HistoryEntry> UndoItems => _undo;

    /// <summary>
    /// Identifies the document content the history is at: the top undo item, 0 before any. Undoing
    /// and redoing back to an item brings its value back; an edit or a merge gives a new one.
    /// </summary>
    public long Position => _undo.Count > 0 ? _undo[^1].Sequence : 0;

    /// <summary>Whether the document differs from the one last saved (or loaded): the history moved away from <see cref="MarkSaved"/>.</summary>
    public bool IsModified => Position != _savedAt;

    /// <summary>
    /// Records that the document at <paramref name="position"/> was saved. The next edit starts an
    /// item of its own, so undoing it returns exactly to the saved content〔默认〕.
    /// </summary>
    public void MarkSaved(long position)
    {
        _savedAt = position;
        if (position == Position)
        {
            _mergeBroken = true;
        }
    }

    /// <summary>Stops the next edit from merging into the last item (caret jumps, other commands).</summary>
    public void BreakMerge() => _mergeBroken = true;

    internal void Push(HistoryEntry entry)
    {
        _redo.Clear();
        if (!_mergeBroken && _undo.Count > 0 && CanMerge(_undo[^1], entry))
        {
            var last = _undo[^1];
            _undo[^1] = last with
            {
                Steps = last.Steps.AddRange(entry.Steps),
                InverseSteps = entry.InverseSteps.AddRange(last.InverseSteps),
                SelectionAfter = entry.SelectionAfter,
                Revision = entry.Revision,
                Time = entry.Time,
                Sequence = ++_sequence,
            };
        }
        else
        {
            _undo.Add(entry with { Sequence = ++_sequence });
        }

        _mergeBroken = entry.Kind is not (TransactionKind.Typing or TransactionKind.Deletion);
    }

    internal HistoryEntry? PopUndo()
    {
        if (_undo.Count == 0)
        {
            return null;
        }

        var entry = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _mergeBroken = true;
        return entry;
    }

    internal HistoryEntry? PopRedo()
    {
        if (_redo.Count == 0)
        {
            return null;
        }

        var entry = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _mergeBroken = true;
        return entry;
    }

    internal void PushRedo(HistoryEntry entry) => _redo.Add(entry);

    internal void PushUndoAfterRedo(HistoryEntry entry) => _undo.Add(entry);

    /// <summary>
    /// After the document changed without an entry of its own (an undo, a redo, a cancelled
    /// composition), the entries now at the top of both stacks expect its new revision.
    /// </summary>
    internal void Rebase(long revision)
    {
        if (_undo.Count > 0)
        {
            _undo[^1] = _undo[^1] with { Revision = revision };
        }

        if (_redo.Count > 0)
        {
            _redo[^1] = _redo[^1] with { Revision = revision };
        }
    }

    private bool CanMerge(HistoryEntry last, HistoryEntry next)
    {
        return next.Kind is TransactionKind.Typing or TransactionKind.Deletion
            && last.Kind == next.Kind
            && last.EditedBlock is not null
            && last.EditedBlock == next.EditedBlock
            && next.Time - last.Time <= MergeWindow
            && next.SelectionBefore.Head.SamePlace(last.SelectionAfter.Head);
    }
}
