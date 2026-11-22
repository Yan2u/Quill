using System.Collections.Immutable;
using Quill.Editor.Documents;
using Quill.Editor.State;

namespace Quill.Editor.Transactions;

/// <summary>
/// What a transaction did, for undo merging (04-commands-and-behaviors.md).
/// </summary>
public enum TransactionKind
{
    /// <summary>Any edit that never merges with another.</summary>
    Edit,

    /// <summary>Typed text; consecutive typing in one place merges.</summary>
    Typing,

    /// <summary>Deleted characters; consecutive deletion in one place merges.</summary>
    Deletion,

    /// <summary>A block conversion made by an input rule; always its own undo item.</summary>
    InputRule,

    /// <summary>A paragraph re-parsed when committed; always its own undo item.</summary>
    Commit,

    /// <summary>Text from an IME composition.</summary>
    Composition,
}

/// <summary>
/// A reversible edit: steps, their inverses, the selection before and after, and metadata
/// (04-commands-and-behaviors.md). <see cref="Result"/> is the document after the steps.
/// </summary>
public sealed record Transaction
{
    public required ImmutableArray<Step> Steps { get; init; }

    /// <summary>Steps that undo <see cref="Steps"/>, in the order to apply them.</summary>
    public required ImmutableArray<Step> InverseSteps { get; init; }

    public required TextSelection SelectionBefore { get; init; }

    public required TextSelection SelectionAfter { get; init; }

    public required Document Before { get; init; }

    public required Document Result { get; init; }

    public TransactionKind Kind { get; init; } = TransactionKind.Edit;

    public DateTimeOffset Time { get; init; }

    /// <summary>For typing and deletion: the leaf edited, used to decide whether to merge.</summary>
    public BlockId? EditedBlock { get; init; }

    public bool ChangesDocument => !Steps.IsEmpty;
}
