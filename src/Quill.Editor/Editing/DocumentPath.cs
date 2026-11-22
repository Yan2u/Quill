using Quill.Editor.Documents;
using Quill.Editor.State;

namespace Quill.Editor.Editing;

/// <summary>
/// The path from the root to a position: at each level a container and either the child that holds
/// the position or, at the last level of a boundary position, the boundary index.
/// </summary>
internal static class DocumentPath
{
    public readonly record struct Step(ContainerBlock Container, int Index, bool IsChild);

    public static List<Step> Of(Document document, Position position)
    {
        BlockId owner = position is TextPosition text ? text.Block : ((BoundaryPosition)position).Parent;
        var location = DocumentTree.Locate(document, owner);
        var path = location.Ancestors.Select(static step => new Step(step.Container, step.Index, IsChild: true)).ToList();
        if (position is BoundaryPosition boundary)
        {
            path.Add(new Step((ContainerBlock)location.Block, boundary.Index, IsChild: false));
        }

        return path;
    }

    /// <summary>
    /// The deepest level whose container holds both ends in different children (or at a boundary):
    /// below it both paths go through the same child.
    /// </summary>
    public static int CommonLevel(List<Step> start, List<Step> end)
    {
        int common = 0;
        while (common + 1 < start.Count && common + 1 < end.Count
            && start[common].IsChild && end[common].IsChild && start[common].Index == end[common].Index)
        {
            common++;
        }

        return common;
    }
}
