namespace Quill.Editor.WinUI.Elements;

/// <summary>
/// A link reference definition: <c>[label]: destination "title"</c> with brackets, colon and quotes
/// drawn (10). An empty title is hidden unless the definition is active (Q35).
/// </summary>
public sealed partial class MdLinkReferenceDefinition : MdDefinitionView
{
    protected override int SlotCount => 3;

    protected override IEnumerable<object> RowParts()
    {
        bool showTitle = IsActive || (Blocks.Count > 2 && Blocks[2] is MdDefinitionSlot { IsEmpty: false });
        return showTitle ? ["[", 0, "]: ", 1, " \"", 2, "\""] : ["[", 0, "]: ", 1];
    }
}
