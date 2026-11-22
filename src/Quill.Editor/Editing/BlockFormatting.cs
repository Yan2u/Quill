using System.Collections.Immutable;
using Quill.Editor.Behaviors;
using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.State;
using Quill.Editor.Transactions;

namespace Quill.Editor.Editing;

/// <summary>
/// The Paragraph menu (M6.0, Typora's shortcuts, behavior confirmed by the user): heading levels, code
/// and math blocks, quotes, lists and the blocks of the Insert menu. Each command is one undo item and
/// keeps the IDs of the text blocks it moves, so the caret stays where it was.
/// </summary>
internal static class BlockFormatting
{
    /// <summary>Ctrl+= raises one level (a paragraph becomes H6, H1 stays); Ctrl+- lowers one (H6 becomes a paragraph).</summary>
    public static int? HeadingLevel(Block leaf, int delta) => (leaf, delta) switch
    {
        (Paragraph, > 0) => 6,
        (Heading { Level: > 1 } heading, > 0) => heading.Level - 1,
        (Heading heading, < 0) => heading.Level == 6 ? 0 : heading.Level + 1,
        _ => null,
    };

    /// <summary>
    /// Ctrl+Shift+K / Ctrl+Shift+M: the selected paragraphs and headings become one code (math) block
    /// holding their lines; in such a block it turns back into paragraphs, one per run of lines between
    /// blank lines〔默认〕.
    /// </summary>
    public static Transaction? ToggleRaw(EditorState state, bool math, BehaviorContext context)
    {
        if (state.Selection.Head is not TextPosition head || DocumentTree.TryFind(state.Document, head.Block) is not LeafBlock leaf)
        {
            return null;
        }

        var builder = context.Begin(state);
        if ((math ? leaf is MathBlock : leaf is CodeBlock or CodeLanguage) && state.Selection.IsCollapsed)
        {
            // A code block goes whole: its container and the language with it (Q91).
            var outer = (Block?)CodeFence.Of(state.Document, leaf) ?? leaf;
            if (outer is CodeFence fence)
            {
                leaf = fence.Code;
                head = head.Block == leaf.Id ? head : new TextPosition(leaf.Id, leaf.Text.Length);
            }

            var location = builder.Locate(outer.Id);
            var paragraphs = new List<Block>();
            TextPosition? caret = null;
            int offset = 0;
            foreach (string group in leaf.Text.Split("\n\n"))
            {
                var paragraph = new Paragraph { Id = builder.NewId(), Text = group.Trim('\n') };
                int leading = group.Length - group.TrimStart('\n').Length;
                if (caret is null && head.Offset <= offset + group.Length)
                {
                    caret = new TextPosition(paragraph.Id, Math.Clamp(head.Offset - offset - leading, 0, paragraph.Text.Length));
                }

                paragraphs.Add(paragraph);
                offset += group.Length + 2;
            }

            builder.RemoveBlocks(location.Parent!.Id, location.Index, 1);
            builder.InsertBlocks(location.Parent.Id, location.Index, [.. paragraphs]);
            builder.SetCaret(caret ?? new TextPosition(paragraphs[0].Id, 0));
            return builder.Build(TransactionKind.Edit, context.Now);
        }

        var blocks = SelectedSiblings(state, static block => block is Paragraph or Heading);
        if (blocks is null)
        {
            return null;
        }

        var (parent, index, selected) = blocks.Value;
        string text = string.Join('\n', selected.Select(static block => ((LeafBlock)block).Text));
        int OffsetOf(Position position)
        {
            int offset = 0;
            foreach (var block in selected)
            {
                if (position is TextPosition at && block.Id == at.Block)
                {
                    return Math.Min(offset + at.Offset, text.Length);
                }

                offset += ((LeafBlock)block).Text.Length + 1;
            }

            return position == state.Selection.Anchor ? 0 : text.Length;
        }

        Block created = math
            ? new MathBlock { Id = builder.NewId(), Text = text, SeparatorBefore = selected[0].SeparatorBefore }
            : CodeFence.Create(builder.NewId, text, string.Empty) with { SeparatorBefore = selected[0].SeparatorBefore };
        var id = created is CodeFence code ? code.Code.Id : created.Id;
        builder.RemoveBlocks(parent, index, selected.Count);
        builder.InsertBlocks(parent, index, created);
        builder.Selection = new TextSelection(new TextPosition(id, OffsetOf(state.Selection.Anchor)), new TextPosition(id, OffsetOf(state.Selection.Head)));
        return builder.Build(TransactionKind.Edit, context.Now);
    }

    /// <summary>Ctrl+Shift+Q: the selected blocks go into one quote; inside a quote it removes the innermost quote level.</summary>
    public static Transaction? ToggleQuote(EditorState state, BehaviorContext context)
    {
        if (state.Selection.Head is not TextPosition head)
        {
            return null;
        }

        var builder = context.Begin(state);
        var location = DocumentTree.Locate(state.Document, head.Block);
        if (location.ContainersInnermostFirst().OfType<BlockQuote>().FirstOrDefault() is { } quote)
        {
            var place = builder.Locate(quote.Id);
            var children = quote.Children.ToArray();
            if (children.Length > 0)
            {
                children[0] = children[0] with { SeparatorBefore = quote.SeparatorBefore };
            }

            builder.RemoveBlocks(place.Parent!.Id, place.Index, 1);
            builder.InsertBlocks(place.Parent.Id, place.Index, children);
            builder.Selection = state.Selection;
            return builder.Build(TransactionKind.Edit, context.Now);
        }

        if (SelectedSiblings(state, static _ => true) is not var (parent, index, selected))
        {
            return null;
        }

        var wrapped = new BlockQuote
        {
            Id = builder.NewId(),
            Children = [.. selected.Select((block, i) => i == 0 ? block with { SeparatorBefore = null } : block)],
            SeparatorBefore = selected[0].SeparatorBefore,
        };
        builder.RemoveBlocks(parent, index, selected.Count);
        builder.InsertBlocks(parent, index, wrapped);
        builder.Selection = state.Selection;
        return builder.Build(TransactionKind.Edit, context.Now);
    }

    /// <summary>
    /// Ctrl+Shift+[ / ] / X: outside a list the selected blocks become the items of one list; in a list
    /// of the same kind the item ends and its blocks return to where the list was (splitting the list);
    /// in a list of another kind the whole list switches kind.
    /// </summary>
    public static Transaction? ToggleList(EditorState state, ListKind kind, BehaviorContext context)
    {
        if (state.Selection.Head is not TextPosition head)
        {
            return null;
        }

        var builder = context.Begin(state);
        var location = DocumentTree.Locate(state.Document, head.Block);
        var item = location.ContainersInnermostFirst().OfType<ListItem>().FirstOrDefault();
        if (item is not null && DocumentTree.Locate(state.Document, item.Id).Parent is ListBlock list)
        {
            if (KindOf(list, item) == kind)
            {
                EndItem(builder, list, item);
            }
            else
            {
                builder.ReplaceBlock(Switch(list, kind));
            }

            builder.Selection = state.Selection;
            return builder.Build(TransactionKind.Edit, context.Now);
        }

        if (SelectedSiblings(state, static block => block is not (ListBlock or Table or FootnoteDefinition or LinkReferenceDefinition or FrontMatter)) is not var (parent, index, selected))
        {
            return null;
        }

        var created = new ListBlock
        {
            Id = builder.NewId(),
            IsOrdered = kind == ListKind.Ordered,
            SeparatorBefore = selected[0].SeparatorBefore,
            Children =
            [
                .. selected.Select((block, i) => (Block)new ListItem
                {
                    Id = builder.NewId(),
                    Task = kind == ListKind.Task && block is Paragraph ? TaskState.Unchecked : TaskState.None,
                    Number = kind == ListKind.Ordered ? i + 1 : null,
                    Children = [block is LeafBlock leaf ? leaf with { SeparatorBefore = null, LinePrefixes = [] } : block with { SeparatorBefore = null }],
                }),
            ],
        };
        builder.RemoveBlocks(parent, index, selected.Count);
        builder.InsertBlocks(parent, index, created);
        builder.Selection = state.Selection;
        return builder.Build(TransactionKind.Edit, context.Now);
    }

    /// <summary>
    /// The Insert menu: a paragraph above or below the block at the caret (a table or definition as a
    /// whole); a thematic break or TOC where a table would go (Ctrl+T's rules), the caret in the text
    /// after it; YAML front matter at the start of the document, or the caret into the existing one.
    /// </summary>
    public static Transaction? Insert(EditorState state, InsertedBlock kind, BehaviorContext context)
    {
        var builder = context.Begin(state);
        switch (kind)
        {
            case InsertedBlock.FrontMatter:
            {
                if (state.Document.Children.Count > 0 && state.Document.Children[0] is FrontMatter existing)
                {
                    builder.SetCaret(existing.Id, existing.Text.Length);
                    return builder.Build(TransactionKind.Edit, context.Now);
                }

                var front = new FrontMatter { Id = builder.NewId(), Text = string.Empty };
                builder.InsertBlocks(state.Document.Id, 0, front);
                builder.SetCaret(front.Id, 0);
                return builder.Build(TransactionKind.Edit, context.Now);
            }

            case InsertedBlock.ParagraphAbove or InsertedBlock.ParagraphBelow:
            {
                if (state.Selection.Head is not TextPosition head || Row(state.Document, head.Block) is not { } target)
                {
                    return null;
                }

                var paragraph = EditOperations.NewParagraph(builder);
                if (kind == InsertedBlock.ParagraphAbove)
                {
                    EditOperations.InsertBefore(builder, target, paragraph);
                }
                else
                {
                    EditOperations.InsertAfter(builder, target, paragraph);
                }

                builder.SetCaret(paragraph.Id, 0);
                return builder.Build(TransactionKind.Edit, context.Now);
            }

            default:
            {
                Block block = kind == InsertedBlock.HorizontalRule
                    ? new ThematicBreak { Id = builder.NewId() }
                    : new TocBlock { Id = builder.NewId() };
                if (!PlaceAtCaret(builder, block))
                {
                    return null;
                }

                // The caret goes into the text after the block, a new paragraph when there is none.
                var place = builder.Locate(block.Id);
                var parent = (ContainerBlock)place.Parent!;
                if (place.Index + 1 < parent.Children.Count && parent.Children[place.Index + 1] is Paragraph next)
                {
                    builder.SetCaret(next.Id, 0);
                }
                else
                {
                    var paragraph = EditOperations.NewParagraph(builder);
                    EditOperations.InsertAfter(builder, block.Id, paragraph);
                    builder.SetCaret(paragraph.Id, 0);
                }

                return builder.Build(TransactionKind.Edit, context.Now);
            }
        }
    }

    /// <summary>
    /// Puts a block where the caret is (Ctrl+T, checked in Typora): an empty paragraph becomes the block;
    /// a paragraph with text is split at the caret with the block between the halves (no empty half);
    /// after a heading〔默认〕. False in cells, code and other raw text.
    /// </summary>
    public static bool PlaceAtCaret(TransactionBuilder builder, Block block)
    {
        EditOperations.DeleteSelection(builder);
        var caret = Behaviors.TextBehavior.EnsureTextCaret(builder);
        switch (builder.Find<LeafBlock>(caret.Block))
        {
            case Paragraph { Text.Length: 0 } empty:
            {
                var location = builder.Locate(empty.Id);
                builder.RemoveBlocks(location.Parent!.Id, location.Index, 1);
                builder.InsertBlocks(location.Parent.Id, location.Index, block);
                return true;
            }

            case Paragraph paragraph when caret.Offset == 0:
                EditOperations.InsertBefore(builder, paragraph.Id, block);
                return true;
            case Paragraph paragraph:
                if (caret.Offset < paragraph.Text.Length)
                {
                    EditOperations.SplitLeaf(builder, paragraph.Id, caret.Offset, static (id, text) => new Paragraph { Id = id, Text = text });
                }

                EditOperations.InsertAfter(builder, paragraph.Id, block);
                return true;
            case Heading heading:
                EditOperations.InsertAfter(builder, heading.Id, block);
                return true;
            default:
                return false;
        }
    }

    /// <summary>The block a leaf stands for among its siblings: a cell's table, a slot's definition or code block, otherwise the leaf.</summary>
    private static BlockId? Row(Document document, BlockId leaf)
    {
        var location = DocumentTree.Locate(document, leaf);
        return location.Block switch
        {
            TableCell => location.ContainersInnermostFirst().OfType<Table>().First().Id,
            DefinitionSlot or CodeBlock or CodeLanguage when location.Parent is FootnoteDefinition or LinkReferenceDefinition or CodeFence => location.Parent!.Id,
            _ => leaf,
        };
    }

    /// <summary>
    /// The blocks the selection covers within one container: from the block of its start to the block
    /// of its end when they are siblings, otherwise the block at the head. Null when one fails <paramref name="accepts"/>.
    /// </summary>
    private static (BlockId Parent, int Index, List<Block> Blocks)? SelectedSiblings(EditorState state, Func<Block, bool> accepts)
    {
        var (start, end) = PositionOrder.Ordered(state.Document, state.Selection);
        if (start is not TextPosition first || end is not TextPosition last || Row(state.Document, first.Block) is not { } from || Row(state.Document, last.Block) is not { } to)
        {
            return null;
        }

        var fromPlace = DocumentTree.Locate(state.Document, from);
        var toPlace = DocumentTree.Locate(state.Document, to);
        if (fromPlace.Parent is not ContainerBlock parent)
        {
            return null;
        }

        int lastIndex = toPlace.Parent?.Id == parent.Id ? toPlace.Index : fromPlace.Index;
        var blocks = parent.Children.Skip(fromPlace.Index).Take(lastIndex - fromPlace.Index + 1).ToList();
        return blocks.All(accepts) ? (parent.Id, fromPlace.Index, blocks) : null;
    }

    private static ListKind KindOf(ListBlock list, ListItem item) =>
        list.IsOrdered ? ListKind.Ordered : item.Task != TaskState.None ? ListKind.Task : ListKind.Bullet;

    /// <summary>The list as another kind: its marker syntax is generated anew; ordered items are numbered from 1.</summary>
    private static ListBlock Switch(ListBlock list, ListKind kind) => list with
    {
        IsOrdered = kind == ListKind.Ordered,
        Syntax = null,
        Children =
        [
            .. list.Children.Cast<ListItem>().Select((item, i) => (Block)(item with
            {
                // A task marker stands in front of the item's paragraph; an item starting with another block has none.
                Task = kind == ListKind.Task && item.Children is [Paragraph, ..] ? (item.Task == TaskState.None ? TaskState.Unchecked : item.Task) : TaskState.None,
                Number = kind == ListKind.Ordered ? i + 1 : null,
                Syntax = null,
            })),
        ],
    };

    /// <summary>Ends a list item: its blocks take its place, the items before and after stay two lists.</summary>
    private static void EndItem(TransactionBuilder builder, ListBlock list, ListItem item)
    {
        var place = builder.Locate(list.Id);
        int at = list.Children.IndexOf(item);
        var before = list.Children.Take(at).ToImmutableList();
        var after = list.Children.Skip(at + 1).ToImmutableList();
        var replacement = new List<Block>();
        if (!before.IsEmpty)
        {
            replacement.Add(list with { Children = before });
        }

        replacement.AddRange(item.Children.Select((block, i) => i == 0 && before.IsEmpty ? block with { SeparatorBefore = list.SeparatorBefore } : block with { SeparatorBefore = null }));
        if (!after.IsEmpty)
        {
            replacement.Add(list with { Id = builder.NewId(), Children = after, SeparatorBefore = null, Syntax = list.Syntax is { } syntax ? syntax with { SeparateFrom = null } : null });
        }

        builder.RemoveBlocks(place.Parent!.Id, place.Index, 1);
        builder.InsertBlocks(place.Parent.Id, place.Index, [.. replacement]);
    }
}
