using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Editing;
using Quill.Editor.State;
using Quill.Editor.Transactions;

namespace Quill.Editor.Behaviors;

/// <summary>
/// Footnote definitions and link reference definitions share one set of key rules (10-elements.md,
/// Q35). Backspace at the start of a slot moves to the end of the slot on its left; in the leftmost
/// slot it turns the definition into a paragraph holding the content (footnote) or the link (the
/// title is dropped). Delete at the end of a slot moves to the start of the slot on its right, and does
/// nothing in the rightmost slot. Enter splits the definition at the caret into two.
/// </summary>
internal sealed class DefinitionSlotBehavior : IBehavior
{
    public Transaction? TryHandle(EditorState state, Command command, BehaviorContext context)
    {
        if (state.Selection.Head is not TextPosition head || DocumentTree.Find(state.Document, head.Block) is not DefinitionSlot slot)
        {
            return null;
        }

        var location = DocumentTree.Locate(state.Document, slot.Id);
        var definition = location.Parent!;
        int index = location.Index;
        int slotCount = definition is FootnoteDefinition ? 2 : 3;
        bool collapsed = state.Selection.IsCollapsed;
        var builder = context.Begin(state);
        switch (command)
        {
            case DeleteBackward when collapsed && head.Offset == 0:
                if (index > 0)
                {
                    var left = (DefinitionSlot)definition.Children[index - 1];
                    builder.SetCaret(left.Id, left.Text.Length);
                    return builder.Build(TransactionKind.Edit, context.Now) ?? NoChange.For(state);
                }

                return ToParagraph(builder, definition, context);

            case DeleteForward when collapsed && head.Offset == slot.Text.Length:
                if (index + 1 < slotCount)
                {
                    builder.SetCaret(definition.Children[index + 1].Id, 0);
                    return builder.Build(TransactionKind.Edit, context.Now) ?? NoChange.For(state);
                }

                return NoChange.For(state);

            case SplitBlock:
                return Split(builder, context);

            // Slots hold a single line; Shift+Enter and Ctrl+Enter do nothing〔默认〕.
            case InsertSoftBreak or SplitBlockAlternate:
                return NoChange.For(state);

            default:
                return null;
        }
    }

    /// <summary>
    /// Backspace in the leftmost slot: the definition becomes a paragraph with its ID, holding the
    /// footnote content or the link destination; further blocks of a footnote follow it.
    /// </summary>
    private static Transaction? ToParagraph(TransactionBuilder builder, ContainerBlock definition, BehaviorContext context)
    {
        string text = definition switch
        {
            FootnoteDefinition footnote => footnote.Content.Text,
            LinkReferenceDefinition link => link.Destination.Text,
            _ => string.Empty,
        };

        var location = builder.Locate(definition.Id);
        var paragraph = new Paragraph { Id = definition.Id, Text = text, SeparatorBefore = definition.SeparatorBefore };
        var rest = definition is FootnoteDefinition ? definition.Children.Skip(2).Select(static block => block with { SeparatorBefore = null }).ToArray() : [];
        builder.RemoveBlocks(location.Parent!.Id, location.Index, 1);
        builder.InsertBlocks(location.Parent.Id, location.Index, [paragraph, .. rest]);
        builder.SetCaret(paragraph.Id, 0);
        return builder.Build(TransactionKind.Edit, context.Now);
    }

    /// <summary>
    /// Enter: the text after the caret, and the slots after it, move to the matching slots of a new
    /// definition inserted after this one; the other slots of the new definition are empty. The caret
    /// goes to the start of the matching slot〔默认〕.
    /// </summary>
    private static Transaction? Split(TransactionBuilder builder, BehaviorContext context)
    {
        EditOperations.DeleteSelection(builder);
        if (builder.Selection.Head is not TextPosition caret || builder.Find<Block>(caret.Block) is not DefinitionSlot)
        {
            return builder.Build(TransactionKind.Edit, context.Now);
        }

        var location = builder.Locate(caret.Block);
        var definition = location.Parent!;
        int index = location.Index;
        int slotCount = definition is FootnoteDefinition ? 2 : 3;
        var texts = new string[slotCount];
        for (int i = 0; i < slotCount; i++)
        {
            var slot = (DefinitionSlot)definition.Children[i];
            texts[i] = i < index ? string.Empty : i == index ? slot.Text[caret.Offset..] : slot.Text;
            int from = i == index ? caret.Offset : 0;
            if (i >= index)
            {
                builder.ReplaceText(slot.Id, from, slot.Text.Length, string.Empty);
            }
        }

        var slots = new List<Block>(slotCount);
        for (int i = 0; i < slotCount; i++)
        {
            slots.Add(new DefinitionSlot { Id = builder.NewId(), SlotKind = ((DefinitionSlot)definition.Children[i]).SlotKind, Text = texts[i] });
        }

        var definitionLocation = builder.Locate(definition.Id);
        ContainerBlock created;
        if (definition is FootnoteDefinition)
        {
            // Further blocks of a multi-paragraph footnote come after the caret: they move along.
            var current = builder.Find<FootnoteDefinition>(definition.Id);
            var rest = current.Children.Skip(2).ToList();
            if (rest.Count > 0)
            {
                builder.RemoveBlocks(definition.Id, 2, rest.Count);
            }

            created = new FootnoteDefinition { Id = builder.NewId(), Children = [.. slots, .. rest] };
        }
        else
        {
            created = new LinkReferenceDefinition { Id = builder.NewId(), Children = [.. slots] };
        }

        builder.InsertBlocks(definitionLocation.Parent!.Id, definitionLocation.Index + 1, created);
        builder.SetCaret(slots[index].Id, 0);
        return builder.Build(TransactionKind.Edit, context.Now);
    }
}
