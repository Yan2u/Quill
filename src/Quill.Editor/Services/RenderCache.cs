using Quill.Editor.Documents;

namespace Quill.Editor.Services;

/// <summary>
/// Runs rendering work off the editing path and delivers the result back on the editor's thread
/// (05: images, formulas, diagrams, highlighting). Tests use a scheduler they advance by hand (08).
/// </summary>
public interface IRenderScheduler
{
    void Schedule(Func<CancellationToken, Task<object?>> work, Action<object?> completed);
}

/// <summary>
/// Asynchronous render results (05): requested by a block, kept by content (kind and source, e.g. an
/// image's resolved path), and dropped when they arrive after the requesting block changed: its
/// view asks again for its new content. The editing core never waits for a result.
/// </summary>
public sealed class RenderCache : IDisposable
{
    private readonly IRenderScheduler _scheduler;
    private readonly Func<Document> _document;
    private readonly Dictionary<(string Kind, string Source), object?> _results = [];
    private readonly HashSet<(string Kind, string Source, BlockId Block, long Revision)> _pending = [];
    private bool _disposed;

    public RenderCache(IRenderScheduler scheduler, Func<Document> document)
    {
        _scheduler = scheduler;
        _document = document;
    }

    /// <summary>Raised on the editor's thread when a result was kept for the block that asked for it.</summary>
    public event Action<BlockId>? ResultReady;

    /// <summary>Results that arrived after their block had changed and were dropped.</summary>
    public int Dropped { get; private set; }

    public int Count => _results.Count;

    /// <summary>The kept result for content, if there is one (null for work that failed).</summary>
    public bool TryGet(string kind, string source, out object? result) => _results.TryGetValue((kind, source), out result);

    /// <summary>
    /// Asks for content on behalf of a block, unless it is kept already or the same block revision
    /// is waiting for it. Returns whether work was scheduled.
    /// </summary>
    public bool Request(BlockId block, string kind, string source, Func<CancellationToken, Task<object?>> work)
    {
        if (_disposed || _results.ContainsKey((kind, source)) || DocumentLookup.Find(_document(), block) is not { } requester)
        {
            return false;
        }

        var key = (kind, source, block, requester.Revision);
        if (!_pending.Add(key))
        {
            return false;
        }

        _scheduler.Schedule(work, result =>
        {
            _pending.Remove(key);
            if (_disposed)
            {
                (result as IDisposable)?.Dispose();
                return;
            }

            if (DocumentLookup.Find(_document(), block) is not { } current || current.Revision != key.Revision)
            {
                // Stale: the block changed while the work ran (05).
                (result as IDisposable)?.Dispose();
                Dropped++;
                return;
            }

            if (_results.TryGetValue((kind, source), out object? existing) && !ReferenceEquals(existing, result))
            {
                (existing as IDisposable)?.Dispose();
            }

            _results[(kind, source)] = result;
            ResultReady?.Invoke(block);
        });
        return true;
    }

    /// <summary>Forgets one result no block shows any longer (an older version of a diagram being edited).</summary>
    public void Forget(string kind, string source) => _results.Remove((kind, source));

    /// <summary>Forgets every result, e.g. after the document's folder changed.</summary>
    public void Clear() => _results.Clear();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (object? value in _results.Values)
        {
            (value as IDisposable)?.Dispose();
        }

        _results.Clear();
        _pending.Clear();
    }
}
