using Quill.Editor.Documents;
using Quill.Editor.Markdown;
using Quill.Editor.State;

namespace Quill.Playground.Samples;

/// <summary>Builds editor states from Markdown for the Editor page and the rendering probes.</summary>
internal static class EditorDocuments
{
    /// <summary>Imports Markdown with the caret at the start of the document.</summary>
    public static EditorState Load(string markdown)
    {
        var document = MarkdownImporter.Import(markdown, new SequentialBlockIdSource()).Document;
        return new EditorState { Document = document, Selection = TextSelection.Caret(FirstCaret(document)) };
    }

    /// <summary>The first text position, or the boundary before the first block without text.</summary>
    public static Position FirstCaret(Document document)
    {
        ContainerBlock container = document;
        while (container.Children.Count > 0)
        {
            switch (container.Children[0])
            {
                case LeafBlock leaf:
                    return new TextPosition(leaf.Id, 0);
                case ContainerBlock { Children.Count: > 0 } child:
                    container = child;
                    break;
                default:
                    return new BoundaryPosition(container.Id, 0);
            }
        }

        return new BoundaryPosition(container.Id, 0);
    }
}
