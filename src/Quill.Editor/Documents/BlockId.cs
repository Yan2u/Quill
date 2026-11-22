namespace Quill.Editor.Documents;

/// <summary>
/// Opaque identity of a block, unique within a document. Identity-preserving edits keep it;
/// new blocks get a fresh one from <see cref="IBlockIdSource"/>.
/// </summary>
public readonly record struct BlockId(long Value)
{
    public override string ToString() => $"#{Value}";
}

/// <summary>
/// Generates block IDs. Injected so that tests can use deterministic sequential IDs.
/// </summary>
public interface IBlockIdSource
{
    BlockId NextId();
}

/// <summary>
/// Hands out increasing IDs starting at a given value. Thread-safe, so a background import can share
/// the session's source.
/// </summary>
public sealed class SequentialBlockIdSource : IBlockIdSource
{
    private long _next;

    public SequentialBlockIdSource(long first = 1)
    {
        _next = first - 1;
    }

    public BlockId NextId() => new(Interlocked.Increment(ref _next));
}
