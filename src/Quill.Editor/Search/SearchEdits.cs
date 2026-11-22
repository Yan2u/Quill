using System.Text;
using Quill.Editor.Behaviors;
using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Inlines;
using Quill.Editor.State;
using Quill.Editor.Transactions;

namespace Quill.Editor.Search;

/// <summary>
/// Replacing matches (12-find-replace.md). The replacement is source text; the normalizer applies the
/// rules of typing (escaped pipes in cells, one-line headings); paragraphs the replacement changed are
/// committed in the same transaction (after the zero-width space rule), so block syntax written by a
/// replacement takes effect at once and the document reopens the same.
/// </summary>
internal static class SearchEdits
{
    /// <summary>Replaces the selection when it is exactly a match; the caret goes after the replacement.</summary>
    public static Transaction? ReplaceSelection(EditorState state, ReplaceMatch command, BehaviorContext context)
    {
        if (DocumentSearch.TryCreate(command.Query, out _) is not { } search || search.At(state.Document, state.Selection) is not { } match
            || DocumentTree.Find(state.Document, match.Block) is not LeafBlock leaf)
        {
            return null;
        }

        string replacement = search.Replacement(leaf, new TextRange(match.Start, match.End), command.Replacement);
        var builder = context.Begin(state);
        builder.ReplaceText(leaf.Id, match.Start, match.End, replacement);
        builder.SetCaret(leaf.Id, match.Start + replacement.Length);
        CommitParagraphs(builder, [(leaf.Id, leaf.Text)]);
        return builder.Build(TransactionKind.Edit, context.Now, leaf.Id);
    }

    /// <summary>Replaces every match in one transaction: one step per leaf (from its first match to its last), one per match in the leaves holding the selection.</summary>
    public static Transaction? ReplaceAll(EditorState state, ReplaceAllMatches command, BehaviorContext context)
    {
        if (DocumentSearch.TryCreate(command.Query, out _) is not { } search)
        {
            return null;
        }

        // The leaves holding the selection get one step per match, so the caret keeps its place among
        // them; the others one step from their first match to their last.
        var selected = new HashSet<BlockId>();
        foreach (var end in new[] { state.Selection.Anchor, state.Selection.Head })
        {
            if (end is TextPosition position)
            {
                selected.Add(position.Block);
            }
        }

        var changes = new List<(BlockId Leaf, int Start, int End, string Text)>();
        foreach (var leaf in Editing.Navigation.Leaves(state.Document))
        {
            var matches = search.MatchesIn(leaf);
            if (matches.Count == 0)
            {
                continue;
            }

            if (selected.Contains(leaf.Id))
            {
                for (int i = matches.Count - 1; i >= 0; i--)
                {
                    changes.Add((leaf.Id, matches[i].Start, matches[i].End, search.Replacement(leaf, matches[i], command.Replacement)));
                }

                continue;
            }

            var text = new StringBuilder();
            int position = matches[0].Start;
            foreach (var range in matches)
            {
                text.Append(leaf.Text, position, range.Start - position);
                text.Append(search.Replacement(leaf, range, command.Replacement));
                position = range.End;
            }

            changes.Add((leaf.Id, matches[0].Start, matches[^1].End, text.ToString()));
        }

        if (changes.Count == 0)
        {
            return null;
        }

        var builder = context.Begin(state);
        foreach (var (leaf, start, end, text) in changes)
        {
            builder.ReplaceText(leaf, start, end, text);
        }

        var before = changes.Select(static change => change.Leaf).Distinct()
            .Select(id => (id, ((LeafBlock)Documents.DocumentTree.Find(state.Document, id)).Text)).ToList();
        CommitParagraphs(builder, before);
        return builder.Build(TransactionKind.Edit, context.Now);
    }

    /// <summary>The changed paragraphs: the zero-width space rule, then a commit where a changed line may now be block syntax.</summary>
    private static void CommitParagraphs(TransactionBuilder builder, IEnumerable<(BlockId Id, string Before)> leaves)
    {
        foreach (var (id, before) in leaves)
        {
            if (DocumentTree.Contains(builder.Document, id) && builder.Find<Block>(id) is Paragraph paragraph)
            {
                Normalizer.ApplyZeroWidthSpaceRule(builder, paragraph);
                if (MayStartBlock(before, builder.Find<Paragraph>(id).Text))
                {
                    Editing.ParagraphCommit.Commit(builder, id);
                }
            }
        }
    }

    /// <summary>
    /// Whether the edited text may parse as more than a paragraph (a commit is needed, 09): a changed
    /// line that is blank, starts with a character that can begin block syntax (headings, quotes, lists,
    /// rules, Setext underlines, fences, math, HTML, definitions), or holds a pipe (a table row). A
    /// committed paragraph had none of those, so lines that did not change cannot start a block.
    /// </summary>
    internal static bool MayStartBlock(string before, string after)
    {
        var old = before.Split('\n');
        var lines = after.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i < old.Length && old[i] == lines[i] && lines.Length == old.Length)
            {
                continue;
            }

            string line = lines[i].TrimStart(' ', '\u200B');
            if (line.Length == 0 || line.Contains('|') || "#>-+*=_`~$<[:".Contains(line[0]) || char.IsDigit(line[0]))
            {
                return true;
            }
        }

        return false;
    }
}
