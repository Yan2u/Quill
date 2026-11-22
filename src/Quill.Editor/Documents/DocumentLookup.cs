namespace Quill.Editor.Documents;

/// <summary>
/// Read-only lookups for the view layer (the WinUI layout service): finding a block by ID through
/// the document index, without walking the tree.
/// </summary>
public static class DocumentLookup
{
    /// <summary>The block with the ID, or null when the document does not hold it.</summary>
    public static Block? Find(Document document, BlockId id) => DocumentTree.TryFind(document, id);

    /// <summary>The index of the top-level block that is or holds the block, or null.</summary>
    public static int? TopLevelIndex(Document document, BlockId id)
    {
        if (id == document.Id || !DocumentTree.Contains(document, id))
        {
            return null;
        }

        return DocumentTree.Locate(document, id).Ancestors[0].Index;
    }
}
