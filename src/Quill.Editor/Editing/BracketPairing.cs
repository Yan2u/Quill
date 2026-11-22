using Quill.Editor.Documents;
using Quill.Editor.State;

namespace Quill.Editor.Editing;

/// <summary>
/// Bracket and quote pairing (04-commands-and-behaviors.md, Q107, Q115). Typing ( [ { " ' adds the closing
/// half after the caret, unless the next character is a letter or digit, or (for quotes) the previous one
/// is, as in "don't". Typing a closing half right before one that was added for the caret steps over it.
/// The closing halves added are remembered while the caret stays in their block, and follow edits there.
/// </summary>
internal sealed class BracketPairing
{
    private const string Openers = "([{\"'";
    private const string Closers = ")]}\"'";

    private readonly List<int> _closers = [];
    private BlockId? _block;

    /// <summary>The closing half of <paramref name="opener"/>, or null when it does not open a pair.</summary>
    public static char? CloserOf(char opener) => Openers.IndexOf(opener) is >= 0 and var index ? Closers[index] : null;

    /// <summary>Whether typing <paramref name="opener"/> at <paramref name="offset"/> of <paramref name="text"/> adds its closing half.</summary>
    public static bool ShouldPair(string text, int offset, char opener)
    {
        if (offset < text.Length && char.IsLetterOrDigit(text[offset]))
        {
            return false;
        }

        return !(opener is '"' or '\'' && offset > 0 && char.IsLetterOrDigit(text[offset - 1]));
    }

    /// <summary>Whether the caret sits between an opening half and its closing half: Backspace deletes both.</summary>
    public static bool IsEmptyPair(string text, int offset) =>
        offset > 0 && offset < text.Length && CloserOf(text[offset - 1]) == text[offset];

    /// <summary>Whether the character at <paramref name="offset"/> of the block is a closing half added for the caret.</summary>
    public bool IsAdded(BlockId block, int offset) => _block == block && _closers.Contains(offset);

    /// <summary>Remembers a closing half added at <paramref name="offset"/>.</summary>
    public void Add(BlockId block, int offset)
    {
        if (_block != block)
        {
            _closers.Clear();
            _block = block;
        }

        if (!_closers.Contains(offset))
        {
            _closers.Add(offset);
        }
    }

    /// <summary>Forgets a closing half (stepped over).</summary>
    public void Remove(int offset) => _closers.Remove(offset);

    public void Clear()
    {
        _closers.Clear();
        _block = null;
    }

    /// <summary>
    /// Follows a change: the closing halves are forgotten when the caret leaves their block, and shift with an
    /// edit inside it (one replaced range, found by comparing the block's text before and after: the work is
    /// in proportion to the block, not to the document, 09).
    /// </summary>
    public void Follow(Document before, EditorState after)
    {
        if (_block is not { } block)
        {
            return;
        }

        if (after.Selection is not { IsCollapsed: true, Head: TextPosition head } || head.Block != block
            || DocumentTree.TryFind(after.Document, block) is not LeafBlock now)
        {
            Clear();
            return;
        }

        if (ReferenceEquals(before, after.Document) || DocumentTree.TryFind(before, block) is not LeafBlock was || was.Text == now.Text)
        {
            return;
        }

        string old = was.Text;
        string text = now.Text;
        int prefix = 0;
        int limit = Math.Min(old.Length, text.Length);
        while (prefix < limit && old[prefix] == text[prefix])
        {
            prefix++;
        }

        int suffix = 0;
        while (suffix < limit - prefix && old[^(suffix + 1)] == text[^(suffix + 1)])
        {
            suffix++;
        }

        int removedEnd = old.Length - suffix;
        int delta = text.Length - old.Length;
        for (int i = _closers.Count - 1; i >= 0; i--)
        {
            int offset = _closers[i];
            int moved = offset >= removedEnd ? offset + delta : offset < prefix ? offset : -1;
            if (moved < 0 || moved >= text.Length || text[moved] != old[offset])
            {
                _closers.RemoveAt(i);
            }
            else
            {
                _closers[i] = moved;
            }
        }
    }
}
