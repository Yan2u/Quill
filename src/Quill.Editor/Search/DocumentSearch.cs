using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Quill.Editor.Documents;
using Quill.Editor.Editing;
using Quill.Editor.Inlines;
using Quill.Editor.Projection;
using Quill.Editor.State;

namespace Quill.Editor.Search;

/// <summary>What to look for (12-find-replace.md): the text and the three options, all off by default.</summary>
public sealed record SearchQuery(string Text, bool MatchCase = false, bool WholeWord = false, bool UseRegex = false);

/// <summary>A match: a range of a leaf's source text.</summary>
public readonly record struct SearchMatch(BlockId Block, int Start, int End)
{
    public int Length => End - Start;
}

/// <summary>
/// The matches of one query in a document (12-find-replace.md): every leaf's source text is searched,
/// in document order, so inline syntax markers and link URLs are found and block syntax (kept outside
/// the leaves) is not; in a source document the leaves are its lines. Matches never cross a leaf and
/// are never empty. The matches of a leaf are cached by the leaf object, so after an edit only the
/// changed leaves are searched again. Immutable documents make it safe on any thread.
/// </summary>
public sealed class DocumentSearch
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);
    private static readonly ConditionalWeakTable<Document, LeafOrder> Orders = new();

    private readonly Regex _regex;
    private readonly ConditionalWeakTable<LeafBlock, TextRange[]> _matches = new();

    private DocumentSearch(SearchQuery query, Regex regex)
    {
        Query = query;
        _regex = regex;
    }

    public SearchQuery Query { get; }

    /// <summary>The search of a query, or null for an empty text or an invalid expression (<paramref name="error"/> says why).</summary>
    public static DocumentSearch? TryCreate(SearchQuery query, out string? error)
    {
        error = null;
        if (query.Text.Length == 0)
        {
            return null;
        }

        var options = RegexOptions.CultureInvariant | (query.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase);
        try
        {
            string pattern = query.UseRegex ? query.Text : Regex.Escape(query.Text);
            return new DocumentSearch(query, new Regex(pattern, options, Timeout));
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return null;
        }
    }

    /// <summary>The matches in a leaf's source, in order.</summary>
    public IReadOnlyList<TextRange> MatchesIn(LeafBlock leaf) => _matches.GetValue(leaf, Search);

    /// <summary>All matches of the document, in document order.</summary>
    public IEnumerable<SearchMatch> All(Document document)
    {
        foreach (var leaf in LeafOrder.Of(document).Leaves)
        {
            foreach (var range in MatchesIn(leaf))
            {
                yield return new SearchMatch(leaf.Id, range.Start, range.End);
            }
        }
    }

    /// <summary>The match the selection is exactly, or null.</summary>
    public SearchMatch? At(Document document, TextSelection selection)
    {
        if (!Holds(document, selection))
        {
            return null;
        }

        var (start, end) = PositionOrder.Ordered(document, selection);
        if (start is not TextPosition s || end is not TextPosition e || s.Block != e.Block || s.Offset == e.Offset
            || DocumentTree.TryFind(document, s.Block) is not LeafBlock leaf)
        {
            return null;
        }

        foreach (var range in MatchesIn(leaf))
        {
            if (range.Start == s.Offset && range.End == e.Offset)
            {
                return new SearchMatch(leaf.Id, range.Start, range.End);
            }
        }

        return null;
    }

    /// <summary>
    /// The number of matches and the 1-based number of the match the selection is (0 when it is none).
    /// </summary>
    public (int Count, int Current) Count(Document document, TextSelection selection)
    {
        var current = At(document, selection);
        int count = 0, number = 0;
        foreach (var leaf in LeafOrder.Of(document).Leaves)
        {
            var matches = MatchesIn(leaf);
            if (current is { } match && match.Block == leaf.Id)
            {
                for (int i = 0; i < matches.Count; i++)
                {
                    if (matches[i].Start == match.Start)
                    {
                        number = count + i + 1;
                    }
                }
            }

            count += matches.Count;
        }

        return (count, number);
    }

    /// <summary>
    /// The next match after the selection's end (<paramref name="forward"/>), or the last one before its
    /// start; past either end of the document the search goes on from the other end. Null without matches.
    /// </summary>
    public SearchMatch? Next(Document document, TextSelection selection, bool forward)
    {
        var order = LeafOrder.Of(document);
        if (order.Leaves.Count == 0)
        {
            return null;
        }

        // A selection of another document (the one shown before) searches from the start.
        var (start, end) = Holds(document, selection) ? PositionOrder.Ordered(document, selection) : (selection.Anchor, selection.Head);
        var (leafIndex, offset, inside) = order.Cursor(document, forward ? end : start);
        int count = order.Leaves.Count;

        // Where the walk begins: the leaf holding the position (only the matches on the right side of it
        // count the first time), or the leaf after (before) a position between blocks.
        int begin = inside ? leafIndex : forward ? leafIndex % count : (leafIndex - 1 + count) % count;
        for (int step = 0; step <= count; step++)
        {
            int index = forward ? (begin + step) % count : ((begin - step) % count + count) % count;
            bool restricted = inside && step == 0;
            var leaf = order.Leaves[index];
            var matches = MatchesIn(leaf);
            if (forward)
            {
                foreach (var range in matches)
                {
                    if (!restricted || range.Start >= offset)
                    {
                        return new SearchMatch(leaf.Id, range.Start, range.End);
                    }
                }
            }
            else
            {
                for (int i = matches.Count - 1; i >= 0; i--)
                {
                    if (!restricted || matches[i].End <= offset)
                    {
                        return new SearchMatch(leaf.Id, matches[i].Start, matches[i].End);
                    }
                }
            }
        }

        return null;
    }

    /// <summary>The text that replaces a match: the replacement, with $1, ${name} and $$ expanded for an expression.</summary>
    public string Replacement(LeafBlock leaf, TextRange range, string replacement)
    {
        if (!Query.UseRegex)
        {
            return replacement;
        }

        try
        {
            var match = _regex.Match(leaf.Text, range.Start);
            return match.Success && match.Index == range.Start && match.Length == range.Length ? match.Result(replacement) : replacement;
        }
        catch (RegexMatchTimeoutException)
        {
            return replacement;
        }
    }

    private TextRange[] Search(LeafBlock leaf)
    {
        string text = leaf.Text;

        // The language of a code block is part of the fence, which find does not search (Q86).
        if (leaf is CodeLanguage)
        {
            return [];
        }

        // The zero-width space put before a paragraph so that it does not reopen as code is not text (Q28).
        int from = leaf is TextLeafBlock textLeaf && LeafRenderModel.HidesLeadingZeroWidthSpace(textLeaf) ? 1 : 0;
        if (text.Length <= from)
        {
            return [];
        }

        var found = new List<TextRange>();
        try
        {
            for (var match = _regex.Match(text, from); match.Success; match = match.NextMatch())
            {
                if (match.Length > 0 && (!Query.WholeWord || IsWholeWord(text, match.Index, match.Index + match.Length)))
                {
                    found.Add(new TextRange(match.Index, match.Index + match.Length));
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // A runaway expression: the leaf counts as without matches.
        }

        return [.. found];
    }

    /// <summary>Whether both ends of a selection are positions of the document.</summary>
    private static bool Holds(Document document, TextSelection selection) => Holds(document, selection.Anchor) && Holds(document, selection.Head);

    private static bool Holds(Document document, Position position) => position switch
    {
        TextPosition text => DocumentTree.Contains(document, text.Block),
        BoundaryPosition boundary => DocumentTree.Contains(document, boundary.Parent),
        _ => false,
    };

    private static bool IsWholeWord(string text, int start, int end) =>
        (start == 0 || !IsWordCharacter(text[start - 1]) || !IsWordCharacter(text[start]))
        && (end == text.Length || !IsWordCharacter(text[end]) || !IsWordCharacter(text[end - 1]));

    private static bool IsWordCharacter(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>The leaves of a document in order, with their positions, built once per document.</summary>
    private sealed class LeafOrder
    {
        private readonly Dictionary<BlockId, int> _index;

        private LeafOrder(Document document)
        {
            Leaves = [.. Navigation.Leaves(document)];
            _index = new Dictionary<BlockId, int>(Leaves.Count);
            for (int i = 0; i < Leaves.Count; i++)
            {
                _index[Leaves[i].Id] = i;
            }
        }

        public List<LeafBlock> Leaves { get; }

        public static LeafOrder Of(Document document) => Orders.GetValue(document, static document => new LeafOrder(document));

        /// <summary>
        /// A position as a leaf index and an offset in it (<c>Inside</c>); a position between blocks gives the
        /// index of the next leaf (the leaf count after the last).
        /// </summary>
        public (int Leaf, int Offset, bool Inside) Cursor(Document document, Position position)
        {
            if (position is TextPosition text && _index.TryGetValue(text.Block, out int index))
            {
                return (index, text.Offset, true);
            }

            if (position is not BoundaryPosition boundary || !DocumentTree.Contains(document, boundary.Parent))
            {
                // Not a position of this document: from the start.
                return (0, 0, false);
            }

            // The first leaf at or after the position.
            int low = 0, high = Leaves.Count;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (PositionOrder.Compare(document, new TextPosition(Leaves[middle].Id, 0), position) >= 0)
                {
                    high = middle;
                }
                else
                {
                    low = middle + 1;
                }
            }

            return (low, 0, false);
        }
    }
}
