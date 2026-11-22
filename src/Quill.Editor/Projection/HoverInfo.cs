using Quill.VariableXaml.Model;

namespace Quill.Editor.Projection;

/// <summary>
/// The hover flyout declared on the document node (05: attached nodes): the element's place in its
/// block in display offsets, and what the flyout says (10, Q29). A view decoration, not document content.
/// </summary>
public sealed record HoverInfo(string BlockKey, int DisplayStart, int DisplayEnd, string Text, bool IsWarning) : IStructuralHash
{
    public ulong StructuralHash =>
        NodeHash.Combine(NodeHash.Combine(NodeHash.Combine(NodeHash.Seed, BlockKey), ((ulong)(uint)DisplayStart << 32) | (uint)DisplayEnd), Text + (IsWarning ? "!" : string.Empty));
}
