using System.Collections.Immutable;
using Quill.Editor.Behaviors;
using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Projection;
using Quill.Editor.Search;
using Quill.Editor.Services;
using Quill.Editor.State;
using Quill.Editor.Text;
using Quill.Editor.Transactions;

namespace Quill.Editor.Editing;

/// <summary>
/// Services injected into an editing session (01-architecture.md).
/// </summary>
public sealed record EditorServices
{
    public required IBlockIdSource Ids { get; init; }

    public IClock Clock { get; init; } = SystemClock.Instance;

    public ILayoutService? Layout { get; init; }

    public EditorOptions Options { get; init; } = EditorOptions.Default;
}

/// <summary>A document to save and the history position it belongs to (<see cref="EditorSession.PrepareSave"/>).</summary>
public sealed record SaveSnapshot(Document Document, long Position);

/// <summary>
/// The mutable holder of the editor state and the undo history (03-editor-state.md). Every change
/// goes through <see cref="Execute"/>: selection commands change the selection, edits are turned into
/// transactions by the behaviors, input rules and paragraph commits follow as their own undo items.
/// Called on the UI thread only; the state it exposes is an immutable snapshot.
/// </summary>
public sealed partial class EditorSession
{
    private readonly BehaviorRegistry _behaviors;
    private BehaviorContext _context;
    private BlockId? _dirtyParagraph;
    private List<Transaction>? _composition;

    public EditorSession(EditorState state, EditorServices services, BehaviorRegistry? behaviors = null)
    {
        Services = services;
        _behaviors = behaviors ?? DefaultBehaviors.Create();
        _context = new BehaviorContext { Ids = services.Ids, Clock = services.Clock, Layout = services.Layout, Options = services.Options };
        State = state with { Selection = SelectionNormalizer.Normalize(state.Document, state.Selection) };
    }

    public EditorState State { get; private set; }

    public UndoHistory History { get; } = new();

    public EditorServices Services { get; private set; }

    /// <summary>The settings changed (14-settings.md): later commands follow the new options.</summary>
    public void SetOptions(EditorOptions options)
    {
        if (options != Services.Options)
        {
            Services = Services with { Options = options };
            _context = new BehaviorContext { Ids = Services.Ids, Clock = Services.Clock, Layout = Services.Layout, Options = options };
            _pairs.Clear();
        }
    }

    /// <summary>The memoizing projector of this session (05-projection-and-view.md).</summary>
    public DocumentProjector Projector { get; } = new();

    /// <summary>Projects the current state; unchanged blocks keep their nodes.</summary>
    public ProjectionResult Project() => Projector.Project(State);

    public event EventHandler? StateChanged;

    /// <summary>Whether the document differs from the one loaded or last saved (the title bar's marker).</summary>
    public bool IsModified => History.IsModified || _composition is { Count: > 0 };

    /// <summary>
    /// Prepares a save (06): commits the paragraph at the caret so no uncommitted state is written,
    /// then returns the immutable document to serialize, possibly on another thread (09, rule 5).
    /// </summary>
    public SaveSnapshot PrepareSave()
    {
        CommitDirtyParagraph();
        return new SaveSnapshot(State.Document, History.Position);
    }

    /// <summary>The snapshot was written: the document is unmodified while the history stays at it.</summary>
    public void MarkSaved(SaveSnapshot snapshot) => History.MarkSaved(snapshot.Position);

    public void Execute(Command command)
    {
        switch (command)
        {
            case Undo:
                ApplyHistory(undo: true);
                break;
            case Redo:
                ApplyHistory(undo: false);
                break;
            case SetSelection set:
                ChangeSelection(set.Selection, goalX: null, exact: set.Exact);
                break;
            case ReplaceMatch or ReplaceAllMatches when _composition is null:
                if ((command is ReplaceMatch one ? SearchEdits.ReplaceSelection(State, one, _context) : SearchEdits.ReplaceAll(State, (ReplaceAllMatches)command, _context)) is { } replaced)
                {
                    Apply(replaced);
                    History.BreakMerge();
                    if (_dirtyParagraph is { } dirty && !DocumentTree.Contains(State.Document, dirty))
                    {
                        _dirtyParagraph = null;
                    }
                }

                break;
            case ReplaceMatch or ReplaceAllMatches:
                break;
            case SelectAll:
                ChangeSelection(ScopeSelection.All(State), goalX: null);
                break;
            case SelectScope when ScopeSelection.Scope(State) is { } scope:
                ChangeSelection(scope, goalX: null);
                break;
            case SelectLine when ScopeSelection.Line(State, Services.Layout) is { } line:
                ChangeSelection(line, goalX: null);
                break;
            case SelectScope or SelectLine:
                break;
            case SelectWordAtCaret when State.Selection is { IsCollapsed: true, Head: TextPosition at }:
                Execute(new SelectWord(at));
                break;
            case SelectWordAtCaret:
                break;
            case SelectWord { At: TextPosition at } when DocumentTree.TryFind(State.Document, at.Block) is LeafBlock leaf:
                var (wordStart, wordEnd) = TextBoundaries.WordAt(leaf.Text, at.Offset);
                ChangeSelection(new TextSelection(at with { Offset = wordStart, Affinity = Affinity.Forward }, at with { Offset = wordEnd, Affinity = Affinity.Backward }), goalX: null);
                break;
            case SelectBlock { At: TextPosition at } when DocumentTree.TryFind(State.Document, at.Block) is LeafBlock leaf:
                ChangeSelection(new TextSelection(new TextPosition(leaf.Id, 0), new TextPosition(leaf.Id, leaf.Text.Length, Affinity.Backward)), goalX: null);
                break;
            case SelectWord or SelectBlock:
                break;
            case GoToFootnoteDefinition go:
                GoTo(DocumentNavigation.FootnoteDefinition(State.Document, go.Label));
                break;
            case GoToFootnoteReference go:
                GoTo(DocumentNavigation.FootnoteReference(State.Document, go.Label, go.Index));
                break;
            case GoToAnchor go:
                GoTo(DocumentNavigation.Anchor(State.Document, go.Fragment));
                break;
            case MoveCaret move:
                var (selection, goalX) = CaretMovement.Move(State, move, Services.Layout);
                ChangeSelection(selection, goalX);
                break;
            case SetInteraction set:
                SetInteraction(set.Interaction);
                break;
            case CommitParagraph:
                CommitDirtyParagraph();
                break;
            case ReloadDocument reload:
                _dirtyParagraph = null;
                Apply(DocumentReload.Build(State, reload.Reloaded, Services.Ids, Services.Clock.Now));
                break;
            case CompositionStart:
                StartComposition();
                break;
            case CompositionEnd end:
                EndComposition(end.Cancel);
                break;
            case InsertText insert when TryTypePaired(insert):
                break;
            case Cut when Services.Options.CopyLineWithoutSelection && State.Selection.IsCollapsed && _composition is null:
                if (LineClipboard.Cut(State, _context) is { } cut)
                {
                    Apply(cut);
                    AfterEdit();
                }

                break;
            case DeleteBackward delete when TryDeletePair(delete):
                break;
            case ReplaceRange replace when _composition is not null:
                Compose(replace);
                break;
            default:
                Edit(command);
                break;
        }
    }

    /// <summary>The selection as clipboard content: plain text and Quill's Markdown (Q9, Q20, Q102); without a selection, the line (Q110).</summary>
    public ClipboardContent Copy() => Clipboard.Copy(State, Services.Options);

    /// <summary>Edit > Copy as plain text (Q102): the text as shown, whatever the options say, and no private format.</summary>
    public ClipboardContent CopyAsPlainText() => Clipboard.Copy(State, Services.Options, displayed: true) with { Markdown = null };

    /// <summary>"Copy table" (Q77): the Markdown of the table at the caret, as plain text and private format〔默认〕; null outside a table.</summary>
    public ClipboardContent? CopyTable()
    {
        if (State.Selection.Head is not TextPosition head || DocumentTree.TryFind(State.Document, head.Block) is not TableCell)
        {
            return null;
        }

        string markdown = Markdown.MarkdownSerializer.SerializeFragment([TableEdits.TableOf(State.Document, head.Block)], Services.Options);
        return new ClipboardContent(markdown, markdown);
    }

    /// <summary>Ctrl+Shift+C: the selection's Markdown as plain text.</summary>
    public ClipboardContent CopyAsMarkdown()
    {
        var content = Clipboard.Copy(State, Services.Options);
        return content with { PlainText = content.Markdown ?? string.Empty };
    }

    private void Edit(Command command)
    {
        if (command is SplitBlock && _composition is null)
        {
            // Enter rules see the raw paragraph; otherwise the paragraph is committed before the Enter.
            var rule = InputRules.TryConvertOnEnter(State, Services.Ids, Services.Clock.Now);
            if (rule is not null)
            {
                Apply(rule);
                _dirtyParagraph = null;
                return;
            }

            CommitDirtyParagraph();
        }

        var transaction = _behaviors.Dispatch(State, command, _context);
        if (transaction is null)
        {
            return;
        }

        Apply(transaction);
        if (transaction.ChangesDocument && _composition is null)
        {
            AfterEdit();
        }
    }

    /// <summary>Input rules for the edited paragraph, then the commit of a paragraph the edit moved away from.</summary>
    private void AfterEdit()
    {
        for (int attempt = 0; attempt < 4 && HeadParagraph() is { } paragraph; attempt++)
        {
            var rule = InputRules.TryConvert(State, paragraph, Services.Ids, Services.Clock.Now);
            if (rule is null)
            {
                break;
            }

            Apply(rule);
        }

        var head = HeadParagraph();
        if (_dirtyParagraph is { } dirty && dirty != head && DocumentTree.Contains(State.Document, dirty))
        {
            Commit(dirty);
        }

        _dirtyParagraph = head;
    }

    private void Apply(Transaction transaction)
    {
        State = State with { Document = transaction.Result, Selection = transaction.SelectionAfter, GoalX = null };
        _pairs.Follow(transaction.Before, State);
        if (transaction.ChangesDocument)
        {
            if (_composition is not null)
            {
                _composition.Add(transaction);
            }
            else
            {
                History.Push(HistoryEntry.From(transaction));
            }
        }
        else
        {
            History.BreakMerge();
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void GoTo(Position? target)
    {
        if (target is not null)
        {
            ChangeSelection(TextSelection.Caret(target), goalX: null);
        }
    }

    private void ChangeSelection(TextSelection selection, double? goalX, bool exact = false)
    {
        // Markers follow the selection by the active set at the time it forms (Q18), unless it is exact.
        var active = Activation.Compute(State);
        selection = SelectionNormalizer.Normalize(State.Document, selection);
        if (!exact)
        {
            selection = InlineSelection.Extend(State.Document, selection, active);
        }
        if (_dirtyParagraph is { } dirty && !(selection.Head is TextPosition head && head.Block == dirty))
        {
            var commit = Commit(dirty);
            if (commit is not null)
            {
                selection = SelectionNormalizer.Normalize(State.Document, MapThrough(commit, selection));
            }
        }

        State = State with { Selection = selection, GoalX = goalX };
        _pairs.Follow(State.Document, State);
        History.BreakMerge();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CommitDirtyParagraph()
    {
        if (_dirtyParagraph is { } dirty)
        {
            Commit(dirty);
        }
    }

    private Transaction? Commit(BlockId paragraph)
    {
        _dirtyParagraph = null;
        var commit = ParagraphCommit.TryCommit(State, paragraph, Services.Ids, Services.Clock.Now);
        if (commit is not null)
        {
            Apply(commit);
        }

        return commit;
    }

    private BlockId? HeadParagraph() =>
        State.Selection.Head is TextPosition head && DocumentTree.Contains(State.Document, head.Block)
            && DocumentTree.Find(State.Document, head.Block) is Paragraph ? head.Block : null;

    private void SetInteraction(Interaction interaction)
    {
        // While the pointer is down the active elements stay as they were (03-editor-state.md).
        var frozen = interaction.IsPointerDown
            ? State.Interaction.FrozenActivationHead ?? State.Selection.Head
            : null;
        State = State with { Interaction = interaction with { FrozenActivationHead = frozen } };
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyHistory(bool undo)
    {
        if (_composition is not null)
        {
            return;
        }

        var entry = undo ? History.PopUndo() : History.PopRedo();
        if (entry is null)
        {
            return;
        }

        _pairs.Clear();

        // The steps apply to an immutable document: a failure leaves the state and the history as they were.
        var document = State.Document;
        try
        {
            if (document.Revision != entry.Revision)
            {
                throw new InvalidOperationException($"The document is at revision {document.Revision}, the history entry expects {entry.Revision}.");
            }

            long revision = document.Revision + 1;
            foreach (var step in undo ? entry.InverseSteps : entry.Steps)
            {
                document = step.Apply(document, revision, out _);
            }

            if (entry.Source is { } edit)
            {
                document = ApplySourceEdit(document, edit, undo);
            }
        }
        catch
        {
            if (undo)
            {
                History.PushUndoAfterRedo(entry);
            }
            else
            {
                History.PushRedo(entry);
            }

            throw;
        }

        var selection = entry.Source is { } source
            ? SourceMode.SelectionAt(document, undo ? source.AnchorBefore : source.AnchorAfter, undo ? source.HeadBefore : source.HeadAfter, Services.Options)
            : undo ? entry.SelectionBefore : entry.SelectionAfter;
        selection = SelectionNormalizer.Normalize(document, selection);
        State = State with { Document = document, Selection = selection, GoalX = null };
        var moved = entry with { Revision = document.Revision };
        if (undo)
        {
            History.PushRedo(moved);
        }
        else
        {
            History.PushUndoAfterRedo(moved);
        }

        History.Rebase(document.Revision);
        _dirtyParagraph = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Leaves for the source mode (06, 11-source-mode.md): commits the paragraph at the caret and returns
    /// the source session, whose state a separate session edits.
    /// </summary>
    public SourceSession EnterSource()
    {
        CommitDirtyParagraph();
        History.BreakMerge();
        return SourceMode.Enter(State.Document, State.Selection, Services.Ids, Services.Options);
    }

    /// <summary>
    /// Comes back from the source mode: the document is rebuilt from the source text, keeping the
    /// unchanged blocks, and every undo item of <paramref name="source"/> becomes an item of this
    /// history that undoes and redoes that change (06). Without changes only the selection follows.
    /// </summary>
    public void LeaveSource(SourceSession session, EditorSession source)
    {
        var items = source.History.UndoItems;
        var documents = new Document[items.Count + 1];
        documents[items.Count] = source.State.Document;
        long revision = source.State.Document.Revision + 1;
        for (int k = items.Count - 1; k >= 0; k--)
        {
            var document = documents[k + 1];
            foreach (var step in items[k].InverseSteps)
            {
                document = step.Apply(document, revision++, out _);
            }

            documents[k] = document;
        }

        string text = SourceText.ToText(source.State.Document);
        if (text == session.Text)
        {
            var selection = SourceMode.MapBack(session.Original, text, source.State.Selection, source.State.Document, Services.Options);
            State = State with { Document = session.Original, Selection = SelectionNormalizer.Normalize(session.Original, selection), GoalX = null };
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        var rebuilt = SourceMode.Rebuild(session.Original, session.Text, session.Starts, text, Services.Ids, Services.Options);
        var mapped = SourceMode.MapBack(rebuilt, text, source.State.Selection, source.State.Document, Services.Options);
        History.BreakMerge();
        for (int k = 0; k < items.Count; k++)
        {
            string before = SourceText.ToText(documents[k]);
            string after = SourceText.ToText(documents[k + 1]);
            if (before == after)
            {
                continue;
            }

            var (start, removed, inserted) = SourceMode.Diff(before, after);
            History.Push(new HistoryEntry
            {
                Steps = [],
                InverseSteps = [],
                SelectionBefore = State.Selection,
                SelectionAfter = mapped,
                Revision = rebuilt.Revision,
                Kind = TransactionKind.Edit,
                Time = items[k].Time,
                Source = new SourceEdit(
                    start,
                    removed,
                    inserted,
                    SourceMode.OffsetOf(documents[k], items[k].SelectionBefore.Anchor),
                    SourceMode.OffsetOf(documents[k], items[k].SelectionBefore.Head),
                    SourceMode.OffsetOf(documents[k + 1], items[k].SelectionAfter.Anchor),
                    SourceMode.OffsetOf(documents[k + 1], items[k].SelectionAfter.Head)),
            });
        }

        History.BreakMerge();
        _dirtyParagraph = null;
        State = State with { Document = rebuilt, Selection = SelectionNormalizer.Normalize(rebuilt, mapped), GoalX = null };
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Undoes or redoes a source edit: the source text of <paramref name="document"/> with the change
    /// reverted or applied, imported with the unchanged blocks kept. A whole-document parse and write,
    /// the one exception to 09 rule 8, accepted for source edits (06).
    /// </summary>
    private Document ApplySourceEdit(Document document, SourceEdit edit, bool undo)
    {
        string text = Markdown.MarkdownSerializer.SerializeNormalized(document, out var starts, Services.Options);
        string expected = undo ? edit.Inserted : edit.Removed;
        if (edit.Start + expected.Length > text.Length || string.CompareOrdinal(text, edit.Start, expected, 0, expected.Length) != 0)
        {
            throw new InvalidOperationException("The source edit does not match the document.");
        }

        string target = text.Remove(edit.Start, expected.Length).Insert(edit.Start, undo ? edit.Removed : edit.Inserted);
        return SourceMode.Rebuild(document, text, starts, target, Services.Ids, Services.Options);
    }

    private void StartComposition()
    {
        if (_composition is not null)
        {
            return;
        }

        // Nothing can be typed on a block caret (Q59): the composition's updates reach the behaviors,
        // which ignore them, instead of deleting the block first.
        if (State.Selection.IsBlockCaret)
        {
            return;
        }

        _composition = [];
        if (!State.Selection.IsCollapsed)
        {
            var builder = _context.Begin(State);
            EditOperations.DeleteSelection(builder);
            var deletion = builder.Build(TransactionKind.Composition, Services.Clock.Now);
            if (deletion is not null)
            {
                Apply(deletion);
            }
        }

        var head = State.Selection.Head;
        var block = head is TextPosition text ? text.Block : default;
        int offset = head is TextPosition at ? at.Offset : 0;
        State = State with { Composition = new Composition(block, offset, 0, State.ActivationHead) };
    }

    private void Compose(ReplaceRange replace)
    {
        var builder = _context.Begin(State);
        var block = replace.Block;
        if (!DocumentTree.Contains(builder.Document, block) && State.Selection.Head is BoundaryPosition boundary)
        {
            // A composition started in a virtual paragraph: the first update makes it a real paragraph.
            var paragraph = EditOperations.NewParagraph(builder);
            builder.InsertBlocks(boundary.Parent, boundary.Index, paragraph);
            block = paragraph.Id;
        }

        builder.ReplaceText(block, replace.From, replace.To, replace.Text);
        builder.SetCaret(block, replace.CaretOffset);
        var transaction = builder.Build(TransactionKind.Composition, Services.Clock.Now, block);
        if (transaction is not null)
        {
            Apply(transaction);
        }

        State = State with { Composition = State.Composition! with { Block = block, Start = replace.From, Length = replace.Text.Length } };
    }

    private void EndComposition(bool cancel)
    {
        var transactions = _composition;
        if (transactions is null)
        {
            return;
        }

        if (!cancel && State.Composition is { } composition && transactions.Count > 0)
        {
            PairComposition(composition);
        }

        _composition = null;
        State = State with { Composition = null };
        if (transactions.Count == 0)
        {
            return;
        }

        if (cancel)
        {
            _pairs.Clear();
            var document = State.Document;
            long revision = document.Revision + 1;
            for (int i = transactions.Count - 1; i >= 0; i--)
            {
                foreach (var step in transactions[i].InverseSteps)
                {
                    document = step.Apply(document, revision, out _);
                }
            }

            State = State with { Document = document, Selection = SelectionNormalizer.Normalize(document, transactions[0].SelectionBefore) };
            History.Rebase(document.Revision);
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        // The whole composition is one undo item (03-editor-state.md).
        History.BreakMerge();
        History.Push(new HistoryEntry
        {
            Steps = [.. transactions.SelectMany(static transaction => transaction.Steps)],
            InverseSteps = [.. Enumerable.Reverse(transactions).SelectMany(static transaction => transaction.InverseSteps)],
            SelectionBefore = transactions[0].SelectionBefore,
            SelectionAfter = transactions[^1].SelectionAfter,
            Revision = State.Document.Revision,
            Kind = TransactionKind.Composition,
            Time = transactions[^1].Time,
        });
        History.BreakMerge();
        AfterEdit();
    }

    /// <summary>Maps a selection of the document before <paramref name="transaction"/> into the document after it.</summary>
    private static TextSelection MapThrough(Transaction transaction, TextSelection selection)
    {
        var document = transaction.Before;
        var anchor = selection.Anchor;
        var head = selection.Head;
        long revision = transaction.Result.Revision;
        foreach (var step in transaction.Steps)
        {
            var next = step.Apply(document, revision, out _);
            anchor = step.Map(anchor, document, next);
            head = step.Map(head, document, next);
            document = next;
        }

        return selection with { Anchor = anchor, Head = head };
    }
}
