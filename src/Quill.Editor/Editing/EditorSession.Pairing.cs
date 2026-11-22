using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.State;
using Quill.Editor.Transactions;

namespace Quill.Editor.Editing;

/// <summary>
/// Bracket and quote pairing in the session (04, Q107, Q115): with <see cref="EditorOptions.MatchBrackets"/>
/// on, typing an opening half adds the closing one in the same undo item, wraps a selection in one block,
/// and steps over a closing half it added; Backspace between an empty pair deletes both. Nothing pairs
/// during an IME composition; a composition that commits a single opening half pairs it then.
/// </summary>
public sealed partial class EditorSession
{
    private readonly BracketPairing _pairs = new();

    /// <summary>Typed text; true when pairing handled it.</summary>
    private bool TryTypePaired(InsertText insert)
    {
        if (!Services.Options.MatchBrackets || _composition is not null || insert.Text.Length != 1)
        {
            return false;
        }

        char typed = insert.Text[0];
        var selection = State.Selection;
        if (selection is { IsCollapsed: true, Head: TextPosition head } && _pairs.IsAdded(head.Block, head.Offset)
            && DocumentTree.TryFind(State.Document, head.Block) is LeafBlock leaf && head.Offset < leaf.Text.Length && leaf.Text[head.Offset] == typed)
        {
            // The closing half added before: step over it instead of typing another.
            _pairs.Remove(head.Offset);
            ChangeSelection(TextSelection.Caret(head.Block, head.Offset + 1), goalX: null);
            return true;
        }

        if (BracketPairing.CloserOf(typed) is not { } closer)
        {
            return false;
        }

        return selection.IsCollapsed ? TryOpenPair(typed, closer) : TryWrap(typed, closer);
    }

    /// <summary>Types the opening half with its closing half after the caret.</summary>
    private bool TryOpenPair(char opener, char closer)
    {
        if (State.Selection.Head is not TextPosition at || DocumentTree.TryFind(State.Document, at.Block) is not LeafBlock leaf
            || !BracketPairing.ShouldPair(leaf.Text, at.Offset, opener))
        {
            return false;
        }

        var transaction = _behaviors.Dispatch(State, new InsertText($"{opener}{closer}"), _context);
        if (transaction is null)
        {
            return true;
        }

        // The behaviors place the caret after what they inserted: it goes back between the two halves.
        BlockId? block = null;
        if (transaction.SelectionAfter is { IsCollapsed: true, Head: TextPosition after } && after.Offset >= 2
            && DocumentTree.TryFind(transaction.Result, after.Block) is LeafBlock result
            && result.Text[after.Offset - 1] == closer && result.Text[after.Offset - 2] == opener)
        {
            transaction = transaction with { SelectionAfter = TextSelection.Caret(after.Block, after.Offset - 1) };
            block = after.Block;
        }

        Apply(transaction);
        if (block is { } id && State.Selection.Head is TextPosition caret && caret.Block == id)
        {
            _pairs.Add(id, caret.Offset);
        }

        if (transaction.ChangesDocument)
        {
            AfterEdit();
        }

        return true;
    }

    /// <summary>Wraps a selection within one block in the pair; the selection stays on the wrapped text.</summary>
    private bool TryWrap(char opener, char closer)
    {
        var selection = State.Selection;
        if (selection is not { Anchor: TextPosition anchor, Head: TextPosition head } || anchor.Block != head.Block
            || DocumentTree.TryFind(State.Document, head.Block) is not LeafBlock)
        {
            return false;
        }

        int start = Math.Min(anchor.Offset, head.Offset);
        int end = Math.Max(anchor.Offset, head.Offset);
        var builder = _context.Begin(State);
        builder.InsertText(head.Block, end, closer.ToString());
        builder.InsertText(head.Block, start, opener.ToString());
        builder.Selection = new TextSelection(anchor with { Offset = anchor.Offset + 1 }, head with { Offset = head.Offset + 1 });
        var transaction = builder.Build(TransactionKind.Edit, Services.Clock.Now, head.Block);
        if (transaction is not null)
        {
            Apply(transaction);
            AfterEdit();
        }

        return true;
    }

    /// <summary>Backspace between an empty pair deletes both halves; true when it did.</summary>
    private bool TryDeletePair(DeleteBackward delete)
    {
        if (!Services.Options.MatchBrackets || _composition is not null || delete.Unit != TextUnit.Grapheme
            || State.Selection is not { IsCollapsed: true, Head: TextPosition at }
            || DocumentTree.TryFind(State.Document, at.Block) is not LeafBlock leaf || !BracketPairing.IsEmptyPair(leaf.Text, at.Offset))
        {
            return false;
        }

        var builder = _context.Begin(State);
        builder.ReplaceText(at.Block, at.Offset - 1, at.Offset + 1, string.Empty);
        builder.SetCaret(at.Block, at.Offset - 1);
        var transaction = builder.Build(TransactionKind.Deletion, Services.Clock.Now, at.Block);
        if (transaction is not null)
        {
            Apply(transaction);
            AfterEdit();
        }

        return true;
    }

    /// <summary>A composition that committed a single opening half: its closing half joins the composition's undo item.</summary>
    private void PairComposition(Composition composition)
    {
        if (!Services.Options.MatchBrackets || composition.Length != 1
            || DocumentTree.TryFind(State.Document, composition.Block) is not LeafBlock leaf || composition.Start >= leaf.Text.Length
            || BracketPairing.CloserOf(leaf.Text[composition.Start]) is not { } closer
            || State.Selection is not { IsCollapsed: true, Head: TextPosition { Offset: var offset } head } || head.Block != leaf.Id || offset != composition.Start + 1
            || !BracketPairing.ShouldPair(leaf.Text, offset, leaf.Text[composition.Start]))
        {
            return;
        }

        var builder = _context.Begin(State);
        builder.InsertText(leaf.Id, offset, closer.ToString());
        builder.SetCaret(leaf.Id, offset);
        if (builder.Build(TransactionKind.Composition, Services.Clock.Now, leaf.Id) is { } transaction)
        {
            Apply(transaction);
            _pairs.Add(leaf.Id, offset);
        }
    }
}
