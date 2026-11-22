using Quill.Editor.Documents;

namespace Quill.Editor.State;

/// <summary>
/// An immutable snapshot of the editor: the single source of truth while editing (01-architecture.md).
/// <see cref="EditorSession"/> replaces it after every command.
/// </summary>
public sealed record EditorState
{
    public required Document Document { get; init; }

    public required TextSelection Selection { get; init; }

    /// <summary>The IME composition in progress, if any.</summary>
    public Composition? Composition { get; init; }

    /// <summary>The horizontal position kept by consecutive vertical caret moves; cleared by other commands.</summary>
    public double? GoalX { get; init; }

    public Interaction Interaction { get; init; } = Interaction.Default;

    /// <summary>A state with the caret at the start of the document, as for a file just opened.</summary>
    public static EditorState AtStart(Document document) => new()
    {
        Document = document,
        Selection = TextSelection.Caret(document.Children.Count > 0 ? Editing.Navigation.First(document, document) : new BoundaryPosition(document.Id, 0)),
    };

    /// <summary>
    /// The position that decides which elements are active (03-editor-state.md): the head, except
    /// while the pointer is down or a composition is in progress, when it stays where it was.
    /// </summary>
    public Position ActivationHead => Interaction.FrozenActivationHead ?? Composition?.ActivationHead ?? Selection.Head;
}

/// <summary>
/// Interaction input. Like the rest of the state it changes only through commands.
/// </summary>
public sealed record Interaction
{
    public static Interaction Default { get; } = new();

    public bool HasFocus { get; init; } = true;

    public bool IsPointerDown { get; init; }

    /// <summary>The activation head kept while the pointer is down; null otherwise.</summary>
    public Position? FrozenActivationHead { get; init; }

    /// <summary>An opaque description of the element under the pointer, for hover effects.</summary>
    public string? HoverTarget { get; init; }
}

/// <summary>
/// An IME composition: its text lies in <see cref="Block"/> from <see cref="Start"/> for
/// <see cref="Length"/> code units. <see cref="ActivationHead"/> stays fixed while it lasts.
/// </summary>
public sealed record Composition(BlockId Block, int Start, int Length, Position ActivationHead);
