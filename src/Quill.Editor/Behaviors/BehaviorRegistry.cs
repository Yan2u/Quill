using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Services;
using Quill.Editor.State;
using Quill.Editor.Transactions;

namespace Quill.Editor.Behaviors;

/// <summary>
/// A rule that turns a command into a transaction for one kind of element
/// (04-commands-and-behaviors.md). Returns null to let the next behavior try. Must be pure: no UI
/// access and no system time (use <see cref="BehaviorContext.Now"/>).
/// </summary>
public interface IBehavior
{
    Transaction? TryHandle(EditorState state, Command command, BehaviorContext context);
}

/// <summary>
/// Injected services available to behaviors.
/// </summary>
public sealed class BehaviorContext
{
    public required IBlockIdSource Ids { get; init; }

    public required IClock Clock { get; init; }

    public ILayoutService? Layout { get; init; }

    public required EditorOptions Options { get; init; }

    public DateTimeOffset Now => Clock.Now;

    public TransactionBuilder Begin(EditorState state) => new(state, Ids);
}

/// <summary>
/// Behaviors registered per element kind, and the dispatch order of 04-commands-and-behaviors.md:
/// the leaf holding the head, then its containers from the inside out, then the generic text
/// behaviors. A head at a block boundary (virtual paragraph) asks the boundary behaviors first.
/// </summary>
public sealed class BehaviorRegistry
{
    private readonly Dictionary<BlockKind, List<IBehavior>> _byKind = [];
    private readonly List<IBehavior> _boundary = [];
    private readonly List<IBehavior> _fallback = [];

    /// <summary>Behaviors run in registration order within a kind.</summary>
    public BehaviorRegistry Register(BlockKind kind, IBehavior behavior)
    {
        if (!_byKind.TryGetValue(kind, out var list))
        {
            list = [];
            _byKind.Add(kind, list);
        }

        list.Add(behavior);
        return this;
    }

    public BehaviorRegistry RegisterBoundary(IBehavior behavior)
    {
        _boundary.Add(behavior);
        return this;
    }

    public BehaviorRegistry RegisterFallback(IBehavior behavior)
    {
        _fallback.Add(behavior);
        return this;
    }

    public Transaction? Dispatch(EditorState state, Command command, BehaviorContext context)
    {
        foreach (var behavior in Chain(state))
        {
            var transaction = behavior.TryHandle(state, command, context);
            if (transaction is not null)
            {
                return transaction;
            }
        }

        return null;
    }

    private IEnumerable<IBehavior> Chain(EditorState state)
    {
        var head = state.Selection.Head;
        BlockId owner;
        if (head is TextPosition text)
        {
            owner = text.Block;
        }
        else
        {
            foreach (var behavior in _boundary)
            {
                yield return behavior;
            }

            owner = ((BoundaryPosition)head).Parent;
        }

        var location = DocumentTree.Locate(state.Document, owner);
        foreach (var behavior in For(location.Block.Kind))
        {
            yield return behavior;
        }

        foreach (var container in location.ContainersInnermostFirst())
        {
            foreach (var behavior in For(container.Kind))
            {
                yield return behavior;
            }
        }

        foreach (var behavior in _fallback)
        {
            yield return behavior;
        }
    }

    private IEnumerable<IBehavior> For(BlockKind kind) =>
        _byKind.TryGetValue(kind, out var list) ? list : [];
}
