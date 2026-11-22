namespace Quill.Editor.Documents;

/// <summary>
/// Checks the document invariants of 02-document-model.md. Tests run it after every import and,
/// from M2 on, after every transaction.
/// </summary>
public static class DocumentValidator
{
    public static IReadOnlyList<string> Validate(Document document)
    {
        var errors = new List<string>();
        var ids = new HashSet<BlockId>();
        if (document.Children.IsEmpty)
        {
            errors.Add("The document has no block.");
        }

        Visit(document, errors, ids);
        return errors;
    }

    private static void Visit(Block block, List<string> errors, HashSet<BlockId> ids)
    {
        if (!ids.Add(block.Id))
        {
            errors.Add($"Duplicate block ID {block.Id}.");
        }

        switch (block)
        {
            case BlockQuote or ListBlock or ListItem when ((ContainerBlock)block).Children.IsEmpty:
                errors.Add($"{block.Kind} {block.Id} has no child.");
                break;
            case ListBlock list when list.Children.Any(static child => child is not ListItem):
                errors.Add($"List {list.Id} has a child that is not a list item.");
                break;
            case Table table:
                ValidateTable(table, errors);
                break;
            case FootnoteDefinition footnote:
                if (footnote.Children.Count < 2
                    || footnote.Children[0] is not DefinitionSlot { SlotKind: DefinitionSlotKind.FootnoteName }
                    || footnote.Children[1] is not DefinitionSlot { SlotKind: DefinitionSlotKind.FootnoteContent } content)
                {
                    errors.Add($"Footnote definition {footnote.Id} does not start with name and content slots.");
                }
                else if (content.Text.Contains('\n'))
                {
                    errors.Add($"Footnote definition {footnote.Id} has a multi-line content slot.");
                }

                break;
            case CodeFence code:
                if (code.Children.Count != 2 || code.Children[0] is not CodeBlock || code.Children[1] is not CodeLanguage { } language)
                {
                    errors.Add($"Code block {code.Id} does not hold code and language.");
                }
                else if (language.Text.Contains('\n'))
                {
                    errors.Add($"Code block {code.Id} has a multi-line language.");
                }

                break;
            case LinkReferenceDefinition definition:
                if (definition.Children.Count != 3
                    || definition.Children[0] is not DefinitionSlot { SlotKind: DefinitionSlotKind.LinkLabel }
                    || definition.Children[1] is not DefinitionSlot { SlotKind: DefinitionSlotKind.LinkDestination }
                    || definition.Children[2] is not DefinitionSlot { SlotKind: DefinitionSlotKind.LinkTitle })
                {
                    errors.Add($"Link reference definition {definition.Id} does not hold label, destination and title slots.");
                }

                break;
            case Heading heading:
                if (heading.Level is < 1 or > 6)
                {
                    errors.Add($"Heading {heading.Id} has level {heading.Level}.");
                }

                if (heading.Text.Contains('\n'))
                {
                    errors.Add($"Heading {heading.Id} contains a line break.");
                }

                break;
        }

        if (block is ContainerBlock container)
        {
            foreach (var child in container.Children)
            {
                if (child is Document)
                {
                    errors.Add($"Document nested in {block.Kind} {block.Id}.");
                }

                Visit(child, errors, ids);
            }
        }
    }

    private static void ValidateTable(Table table, List<string> errors)
    {
        if (table.Children.IsEmpty)
        {
            errors.Add($"Table {table.Id} has no header row.");
            return;
        }

        foreach (var row in table.Children)
        {
            if (row is not TableRow tableRow)
            {
                errors.Add($"Table {table.Id} has a child that is not a row.");
                continue;
            }

            if (tableRow.Children.Count != table.ColumnCount)
            {
                errors.Add($"Row {tableRow.Id} has {tableRow.Children.Count} cells; the table has {table.ColumnCount} columns.");
            }

            foreach (var cell in tableRow.Children)
            {
                if (cell is not TableCell tableCell)
                {
                    errors.Add($"Row {tableRow.Id} has a child that is not a cell.");
                }
                else if (tableCell.Text.Contains('\n'))
                {
                    errors.Add($"Cell {tableCell.Id} contains a line break.");
                }
            }
        }
    }
}
