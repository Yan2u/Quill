namespace Quill.Editor.Diagnostics;

/// <summary>
/// Operations whose cost is proportional to the document length. 09-performance.md requires zero of
/// them per edit and per undo; tests assert that with <see cref="OperationCounters"/>.
/// </summary>
public enum CountedOperation
{
    FullDocumentParse,
    FullDocumentSerialization,
    FullProjection,
}

/// <summary>
/// Counts <see cref="CountedOperation"/>s. Counts go to the scopes active on the current logical call
/// context (so parallel tests do not interfere) and to process-wide totals.
/// </summary>
public static class OperationCounters
{
    private static readonly AsyncLocal<OperationCounterScope?> CurrentScope = new();
    private static readonly long[] Totals = new long[Enum.GetValues<CountedOperation>().Length];

    /// <summary>Starts counting on the current logical call context until the scope is disposed.</summary>
    public static OperationCounterScope BeginScope()
    {
        var scope = new OperationCounterScope(CurrentScope.Value);
        CurrentScope.Value = scope;
        return scope;
    }

    public static long Total(CountedOperation operation) => Interlocked.Read(ref Totals[(int)operation]);

    internal static void Record(CountedOperation operation)
    {
        Interlocked.Increment(ref Totals[(int)operation]);
        for (var scope = CurrentScope.Value; scope is not null; scope = scope.Parent)
        {
            scope.Increment(operation);
        }
    }

    internal static void EndScope(OperationCounterScope scope)
    {
        if (ReferenceEquals(CurrentScope.Value, scope))
        {
            CurrentScope.Value = scope.Parent;
        }
    }
}

/// <summary>
/// Counts recorded while active. Nested scopes also count into their parents.
/// </summary>
public sealed class OperationCounterScope : IDisposable
{
    private readonly int[] _counts = new int[Enum.GetValues<CountedOperation>().Length];

    internal OperationCounterScope(OperationCounterScope? parent)
    {
        Parent = parent;
    }

    internal OperationCounterScope? Parent { get; }

    public int this[CountedOperation operation] => Volatile.Read(ref _counts[(int)operation]);

    public int FullDocumentParses => this[CountedOperation.FullDocumentParse];

    public int FullDocumentSerializations => this[CountedOperation.FullDocumentSerialization];

    public int FullProjections => this[CountedOperation.FullProjection];

    public void Dispose() => OperationCounters.EndScope(this);

    internal void Increment(CountedOperation operation) => Interlocked.Increment(ref _counts[(int)operation]);
}
