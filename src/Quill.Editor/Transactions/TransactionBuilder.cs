using System.Collections.Immutable;
using Quill.Editor.Documents;
using Quill.Editor.Editing;
using Quill.Editor.State;

namespace Quill.Editor.Transactions;

/// <summary>
/// Builds a transaction by applying steps to a working copy of the document right away, so that later
/// steps see the result of earlier ones. Records the inverse of each step and maps the selection.
/// <see cref="Build"/> normalizes the result (02-document-model.md invariants) and the selection.
/// </summary>
public sealed class TransactionBuilder
{
    private readonly List<Step> _steps = [];
    private readonly List<Step> _inverses = [];
    private readonly HashSet<BlockId> _touched = [];

    public TransactionBuilder(EditorState state, IBlockIdSource ids)
    {
        Before = state;
        Ids = ids;
        Document = state.Document;
        Selection = state.Selection;
        Revision = state.Document.Revision + 1;
    }

    public EditorState Before { get; }

    public IBlockIdSource Ids { get; }

    /// <summary>The working document: the state's document with the steps applied so far.</summary>
    public Document Document { get; private set; }

    /// <summary>The selection, mapped through every step; set it to place the result selection.</summary>
    public TextSelection Selection { get; set; }

    /// <summary>The revision given to everything this transaction changes.</summary>
    public long Revision { get; }

    public bool HasSteps => _steps.Count > 0;

    internal IReadOnlyCollection<BlockId> Touched => _touched;

    public void Apply(Step step)
    {
        var before = Document;
        Document = step.Apply(Document, Revision, out var inverse);
        _steps.Add(step);
        _inverses.Add(inverse);
        _touched.Add(step.Target);
        if (step is InsertBlocksStep insert)
        {
            foreach (var block in insert.Blocks)
            {
                _touched.Add(block.Id);
            }
        }

        Selection = new TextSelection(step.Map(Selection.Anchor, before, Document), step.Map(Selection.Head, before, Document));
    }

    public void ReplaceText(BlockId block, int from, int to, string text)
    {
        if (from != to || text.Length > 0)
        {
            Apply(new ReplaceTextStep(block, from, to, text));
        }
    }

    public void InsertText(BlockId block, int offset, string text) => ReplaceText(block, offset, offset, text);

    public void InsertBlocks(BlockId parent, int index, params Block[] blocks)
    {
        if (blocks.Length > 0)
        {
            Apply(new InsertBlocksStep(parent, index, [.. blocks]));
        }
    }

    public void RemoveBlocks(BlockId parent, int index, int count)
    {
        if (count > 0)
        {
            Apply(new RemoveBlocksStep(parent, index, count));
        }
    }

    public void ReplaceBlock(Block replacement) => Apply(new ReplaceBlockStep(replacement));

    public void SetCaret(BlockId block, int offset, Affinity affinity = Affinity.Forward) =>
        Selection = TextSelection.Caret(block, offset, affinity);

    public void SetCaret(Position position) => Selection = TextSelection.Caret(position);

    public T Find<T>(BlockId id)
        where T : Block => DocumentTree.Find<T>(Document, id);

    internal BlockLocation Locate(BlockId id) => DocumentTree.Locate(Document, id);

    public BlockId NewId() => Ids.NextId();

    /// <summary>
    /// Normalizes the document and selection and returns the transaction, or null when nothing
    /// changed (neither the document nor the selection).
    /// </summary>
    public Transaction? Build(TransactionKind kind, DateTimeOffset time, BlockId? editedBlock = null)
    {
        if (_steps.Count > 0)
        {
            Normalizer.Normalize(this);
        }

        Selection = SelectionNormalizer.Normalize(Document, Selection);
        if (_steps.Count == 0 && Selection == Before.Selection)
        {
            return null;
        }

        var inverses = _inverses.ToImmutableArray().Reverse().ToImmutableArray();
        return new Transaction
        {
            Steps = [.. _steps],
            InverseSteps = inverses,
            SelectionBefore = Before.Selection,
            SelectionAfter = Selection,
            Before = Before.Document,
            Result = Document,
            Kind = kind,
            Time = time,
            EditedBlock = editedBlock,
        };
    }
}
